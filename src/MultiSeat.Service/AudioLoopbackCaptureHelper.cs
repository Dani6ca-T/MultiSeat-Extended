using System.Diagnostics;
using System.Runtime.InteropServices;
using MultiSeat.Service.Interop;

namespace MultiSeat.Service;

/// <summary>
/// Records what is being played TO an output endpoint, via WASAPI loopback, and reports the
/// peak amplitude it actually captured.
///
/// Why this exists: <see cref="AudioPeakHelper"/> answers "is audio flowing to this
/// endpoint" — it cannot answer "can audio be captured FROM it". Those are different
/// claims, and the second one is the go/no-go gate for the per-session audio design
/// (#10/#12): the plan is for each seat to render to the private "Remote Audio" endpoint
/// RDP creates inside its session, and for Apollo to loopback-capture that. On some Windows
/// builds capturing from it silently yields nothing, which would sink the design.
///
/// That gate previously required a human driving Audacity inside an RDP session — the last
/// manual step in docs/design/per-session-audio-spike.md, and unrunnable unattended on a
/// headless host. This makes it a number.
///
/// Session-scoped for the same reason as the peak helper: run it INSIDE the session whose
/// audio you are capturing. "Remote Audio" only exists inside its own RDP session.
///
/// ⚠️ An idle endpoint yields NO packets at all rather than packets of silence, so a peak of
/// zero means "nothing was playing" just as much as "capture is broken". Always confirm the
/// source is audible with --audio-peaks first; the spike runbook has this as its own step.
/// </summary>
internal static class AudioLoopbackCaptureHelper
{
    // Mix formats are shared-mode, so the endpoint decides; in practice always 32-bit float.
    private static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT =
        new("00000003-0000-0010-8000-00aa00389b71");

    private const int SubFormatOffset = 24;   // WAVEFORMATEX(18) + union(2) + dwChannelMask(4)

    /// <summary>
    /// Capture <paramref name="seconds"/> of loopback audio from the render endpoint matching
    /// <paramref name="deviceMatch"/> (a friendly-name substring, a full endpoint id, or
    /// "default"), write it to <paramref name="outPath"/> as 16-bit PCM WAV, and print the
    /// peak amplitude. Returns false on any COM failure.
    /// </summary>
    public static bool CaptureLoopback(string deviceMatch, double seconds, string outPath)
    {
        IntPtr formatPtr = IntPtr.Zero;

        try
        {
            var type = Type.GetTypeFromCLSID(ComInterfaces.CLSID_MMDeviceEnumerator, throwOnError: false);
            if (type is null)
            {
                Console.Error.WriteLine("[Loopback] MMDeviceEnumerator not available");
                return false;
            }

            var enumerator = (ComInterfaces.IMMDeviceEnumerator)Activator.CreateInstance(type)!;

            var device = ResolveDevice(enumerator, deviceMatch, out var resolvedName);
            if (device is null)
            {
                Console.Error.WriteLine($"[Loopback] No active render endpoint matching '{deviceMatch}'.");
                Console.Error.WriteLine("[Loopback] Run --audio-peaks to list them.");
                return false;
            }

            Console.WriteLine($"[Loopback] Capturing {seconds:0.#}s from \"{resolvedName}\" "
                            + $"in Windows session {Process.GetCurrentProcess().SessionId}...");

            int hr = device.Activate(typeof(ComInterfaces.IAudioClient).GUID,
                                     ComInterfaces.CLSCTX_ALL, IntPtr.Zero, out var clientObj);
            if (hr != 0) return Fail("Activate(IAudioClient)", hr);

            var client = (ComInterfaces.IAudioClient)clientObj;

            hr = client.GetMixFormat(out formatPtr);
            if (hr != 0) return Fail("GetMixFormat", hr);

            var format = Marshal.PtrToStructure<ComInterfaces.WaveFormatEx>(formatPtr);
            bool isFloat = IsFloatFormat(format, formatPtr);

            Console.WriteLine($"[Loopback] Mix format: {format.nSamplesPerSec} Hz, {format.nChannels} ch, "
                            + $"{format.wBitsPerSample}-bit {(isFloat ? "float" : "PCM")}");

            // Hand the mix format straight back — shared mode requires exactly this format,
            // and passing the original pointer avoids marshalling the EXTENSIBLE tail.
            hr = client.Initialize(
                ComInterfaces.AUDCLNT_SHAREMODE_SHARED,
                ComInterfaces.AUDCLNT_STREAMFLAGS_LOOPBACK,
                ComInterfaces.ReferenceTimesPerSecond,  // 1s buffer; we drain far more often
                0,
                formatPtr,
                IntPtr.Zero);
            if (hr != 0) return Fail("Initialize(LOOPBACK)", hr);

            hr = client.GetService(typeof(ComInterfaces.IAudioCaptureClient).GUID, out var captureObj);
            if (hr != 0) return Fail("GetService(IAudioCaptureClient)", hr);

            var capture = (ComInterfaces.IAudioCaptureClient)captureObj;

            hr = client.Start();
            if (hr != 0) return Fail("Start", hr);

            var samples = new List<float>();
            float peak = 0f, rawPeak = 0f;
            int packets = 0, silentPackets = 0;

            try
            {
                var deadline = Stopwatch.StartNew();
                while (deadline.Elapsed.TotalSeconds < seconds)
                {
                    if (capture.GetNextPacketSize(out uint available) != 0) break;

                    if (available == 0)
                    {
                        // Nothing queued. An idle endpoint produces no packets at all.
                        Thread.Sleep(10);
                        continue;
                    }

                    while (available > 0)
                    {
                        if (capture.GetBuffer(out var data, out uint frames, out uint flags, out _, out _) != 0)
                            break;

                        if (frames > 0)
                        {
                            packets++;
                            bool silent = (flags & ComInterfaces.AUDCLNT_BUFFERFLAGS_SILENT) != 0;
                            if (silent) silentPackets++;

                            // A SILENT packet's buffer contents are undefined — treat as zeros
                            // rather than reading whatever happens to be there.
                            AppendFrames(samples, ref peak, data, frames, format, isFloat, silent);

                            // Diagnostic: also read the buffer ignoring the flag. This is what
                            // separates "the platform handed us silence" from "we discarded real
                            // audio because the flag was set". Without it, a spuriously-set SILENT
                            // flag is indistinguishable from loopback genuinely not working — and
                            // that distinction is the entire point of this tool.
                            if (silent)
                                AppendFrames(null, ref rawPeak, data, frames, format, isFloat, silent: false);
                        }

                        capture.ReleaseBuffer(frames);

                        if (capture.GetNextPacketSize(out available) != 0) break;
                    }
                }
            }
            finally
            {
                client.Stop();
            }

            WriteWav16(outPath, samples, format.nChannels, format.nSamplesPerSec);

            double captured = format.nChannels > 0 && format.nSamplesPerSec > 0
                ? samples.Count / (double)format.nChannels / format.nSamplesPerSec
                : 0;

            Console.WriteLine();
            Console.WriteLine($"[Loopback] packets={packets} (silent={silentPackets})  "
                            + $"captured={captured:0.##}s  ->  {outPath}");
            Console.WriteLine($"[Loopback] peak amplitude: {peak:0.000000}");
            if (silentPackets > 0)
                Console.WriteLine($"[Loopback] raw peak inside SILENT-flagged packets: {rawPeak:0.000000}");
            Console.WriteLine();

            if (packets == 0)
                Console.WriteLine("[Loopback] VERDICT: no packets at all — nothing was playing to this "
                                + "endpoint, OR loopback is unsupported here. Confirm the source with "
                                + "--audio-peaks before concluding.");
            else if (peak > 0.01f)
                Console.WriteLine("[Loopback] VERDICT: real audio captured. Loopback WORKS on this endpoint.");
            else
                Console.WriteLine("[Loopback] VERDICT: packets arrived but they are silent. Either the "
                                + "source was not audible, or loopback yields silence here.");

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Loopback] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            if (formatPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(formatPtr);
        }
    }

    private static bool Fail(string what, int hr)
    {
        Console.Error.WriteLine($"[Loopback] {what} failed hr=0x{hr:X8}");

        // The two failures that actually mean something specific here.
        if ((uint)hr == 0x88890008) // AUDCLNT_E_UNSUPPORTED_FORMAT
            Console.Error.WriteLine("[Loopback] The endpoint rejected its own mix format — loopback "
                                  + "is not usable on it.");
        if ((uint)hr == 0x8889000A) // AUDCLNT_E_DEVICE_IN_USE
            Console.Error.WriteLine("[Loopback] Endpoint is held in exclusive mode by another process.");

        return false;
    }

    /// <summary>
    /// Shared-mode mix formats are WAVE_FORMAT_EXTENSIBLE in practice, so wFormatTag alone
    /// does not say whether samples are float — the SubFormat GUID does.
    /// </summary>
    private static bool IsFloatFormat(ComInterfaces.WaveFormatEx format, IntPtr formatPtr)
    {
        if (format.wFormatTag == ComInterfaces.WAVE_FORMAT_IEEE_FLOAT) return true;
        if (format.wFormatTag != ComInterfaces.WAVE_FORMAT_EXTENSIBLE) return false;
        if (format.cbSize < 22) return false;

        var sub = Marshal.PtrToStructure<Guid>(IntPtr.Add(formatPtr, SubFormatOffset));
        return sub == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
    }

    /// <summary>
    /// Accumulate one packet. <paramref name="samples"/> may be null to measure only —
    /// used for the raw-peak diagnostic, which reads a SILENT-flagged buffer without
    /// letting its contents into the recording.
    /// </summary>
    private static void AppendFrames(
        List<float>? samples, ref float peak, IntPtr data, uint frames,
        ComInterfaces.WaveFormatEx format, bool isFloat, bool silent)
    {
        int count = (int)frames * format.nChannels;

        if (silent)
        {
            if (samples is not null)
                for (int i = 0; i < count; i++) samples.Add(0f);
            return;
        }

        if (isFloat && format.wBitsPerSample == 32)
        {
            var buf = new float[count];
            Marshal.Copy(data, buf, 0, count);
            foreach (var s in buf)
            {
                samples?.Add(s);
                var a = Math.Abs(s);
                if (a > peak) peak = a;
            }
        }
        else if (format.wBitsPerSample == 16)
        {
            var buf = new short[count];
            Marshal.Copy(data, buf, 0, count);
            foreach (var s in buf)
            {
                var f = s / 32768f;
                samples?.Add(f);
                var a = Math.Abs(f);
                if (a > peak) peak = a;
            }
        }
        else if (samples is not null)
        {
            // Unhandled bit depth — keep the frame count honest so the duration is right,
            // rather than silently dropping audio and reporting a short capture.
            for (int i = 0; i < count; i++) samples.Add(0f);
        }
    }

    /// <summary>
    /// Write 16-bit PCM regardless of what we captured. Everything opens it, and it matches
    /// the measuring snippet the spike runbook already documents.
    /// </summary>
    private static void WriteWav16(string path, List<float> samples, ushort channels, uint sampleRate)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        int dataBytes = samples.Count * 2;
        ushort blockAlign = (ushort)(channels * 2);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);

        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);                                    // PCM chunk size
        w.Write(ComInterfaces.WAVE_FORMAT_PCM);
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(sampleRate * blockAlign);               // byte rate
        w.Write(blockAlign);
        w.Write((ushort)16);
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);

        foreach (var s in samples)
        {
            var clamped = Math.Clamp(s, -1f, 1f);
            w.Write((short)(clamped * 32767f));
        }
    }

    private static ComInterfaces.IMMDevice? ResolveDevice(
        ComInterfaces.IMMDeviceEnumerator enumerator, string match, out string resolvedName)
    {
        resolvedName = "";

        if (match.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            if (enumerator.GetDefaultAudioEndpoint(
                    ComInterfaces.EDataFlow.eRender, ComInterfaces.ERole.eConsole, out var def) != 0)
                return null;

            resolvedName = GetFriendlyName(def) ?? "(default)";
            return def;
        }

        if (enumerator.EnumAudioEndpoints(
                ComInterfaces.EDataFlow.eRender, ComInterfaces.DeviceState.Active, out var collection) != 0)
            return null;

        collection.GetCount(out int count);

        for (int i = 0; i < count; i++)
        {
            collection.Item(i, out var device);
            device.GetId(out var id);
            var name = GetFriendlyName(device) ?? "";

            if (string.Equals(id, match, StringComparison.OrdinalIgnoreCase)
                || name.Contains(match, StringComparison.OrdinalIgnoreCase))
            {
                resolvedName = name.Length > 0 ? name : id;
                return device;
            }
        }

        return null;
    }

    private static string? GetFriendlyName(ComInterfaces.IMMDevice device)
    {
        if (device.OpenPropertyStore(ComInterfaces.STGM_READ, out var store) != 0) return null;
        var key = ComInterfaces.PKEY_Device_FriendlyName;
        return store.GetValue(ref key, out var pv) == 0 ? pv.GetString() : null;
    }
}
