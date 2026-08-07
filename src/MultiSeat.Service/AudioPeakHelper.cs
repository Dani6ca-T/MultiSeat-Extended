using System.Diagnostics;
using MultiSeat.Service.Interop;

namespace MultiSeat.Service;

/// <summary>
/// Reports which audio endpoints are actually carrying sound, and which process is
/// producing it, by polling peak meters.
///
/// Why this exists: the MultiSeat host is headless and nobody is ever physically at it,
/// so "play a sound and listen" is not a measurement anyone can take. Worse, the host is
/// reached over RustDesk, which forwards host audio to the operator — so "do you hear it?"
/// measures RustDesk's re-routed stream rather than the endpoint under test, which is a
/// fatal confound when the thing being diagnosed IS audio routing. Peak metering answers
/// "is audio flowing to this endpoint, from whom" objectively and remotely.
///
/// Must run IN the Windows session being measured — same constraint as
/// <see cref="AudioMuteHelper"/>: IAudioSessionManager2::GetSessionEnumerator is
/// session-scoped, so a SYSTEM process in Session 0 sees only Session 0's sessions.
/// To measure a seat or an RDP session, launch this inside that session.
///
/// A peak meter reflects only what is flowing at the instant it is read, so a single
/// sample reads 0 between buffers. Everything here polls over a window and keeps the
/// maximum — a max of 0.0000 across a multi-second window is meaningful silence; one
/// zero sample is not.
/// </summary>
internal static class AudioPeakHelper
{
    /// <summary>
    /// Poll every active render endpoint (and the app sessions on it) for
    /// <paramref name="seconds"/> and print the peak each one reached.
    /// </summary>
    public static bool ReportPeaks(double seconds)
    {
        try
        {
            var type = Type.GetTypeFromCLSID(ComInterfaces.CLSID_MMDeviceEnumerator, throwOnError: false);
            if (type is null)
            {
                Console.Error.WriteLine("[AudioPeak] MMDeviceEnumerator not available");
                return false;
            }

            var enumerator = (ComInterfaces.IMMDeviceEnumerator)Activator.CreateInstance(type)!;

            // Which endpoint is the machine-wide default? The single shared default is the
            // crux of the per-session audio problem, so always show it.
            string? defaultId = null;
            if (enumerator.GetDefaultAudioEndpoint(
                    ComInterfaces.EDataFlow.eRender, ComInterfaces.ERole.eConsole,
                    out var defDevice) == 0)
                defDevice.GetId(out defaultId);

            int hr = enumerator.EnumAudioEndpoints(
                ComInterfaces.EDataFlow.eRender, ComInterfaces.DeviceState.Active, out var collection);
            if (hr != 0)
            {
                Console.Error.WriteLine($"[AudioPeak] EnumAudioEndpoints hr=0x{hr:X8}");
                return false;
            }

            collection.GetCount(out int deviceCount);
            Console.WriteLine($"[AudioPeak] Sampling {deviceCount} active render endpoint(s) for {seconds:0.#}s "
                            + $"in Windows session {Process.GetCurrentProcess().SessionId}...");
            Console.WriteLine();

            var probes = new List<Probe>();

            for (int i = 0; i < deviceCount; i++)
            {
                collection.Item(i, out var device);
                device.GetId(out var deviceId);
                var name = GetFriendlyName(device) ?? "(unnamed)";

                var probe = new Probe
                {
                    DeviceName = name,
                    DeviceId = deviceId,
                    IsDefault = deviceId == defaultId,
                    Meter = ActivateMeter(device),
                };

                // Per-application sessions on this endpoint.
                var iid = typeof(ComInterfaces.IAudioSessionManager2).GUID;
                if (device.Activate(iid, 1, IntPtr.Zero, out var mgrObj) == 0 &&
                    mgrObj is ComInterfaces.IAudioSessionManager2 mgr &&
                    mgr.GetSessionEnumerator(out var sessions) == 0)
                {
                    sessions.GetCount(out int sessionCount);
                    for (int s = 0; s < sessionCount; s++)
                    {
                        if (sessions.GetSession(s, out var session) != 0) continue;
                        session.GetProcessId(out uint pid);
                        if (session is ComInterfaces.IAudioMeterInformation sessionMeter)
                            probe.Sessions.Add(new SessionProbe
                            {
                                Pid = (int)pid,
                                ProcessName = ProcessNameOf((int)pid),
                                Meter = sessionMeter,
                            });
                    }
                }

                probes.Add(probe);
            }

            // Poll. ~20 Hz is plenty to catch a buffer without spinning a core.
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < deadline)
            {
                foreach (var p in probes)
                {
                    p.Peak = Math.Max(p.Peak, ReadPeak(p.Meter));
                    foreach (var s in p.Sessions)
                        s.Peak = Math.Max(s.Peak, ReadPeak(s.Meter));
                }
                Thread.Sleep(50);
            }

            foreach (var p in probes)
            {
                var marker = p.IsDefault ? " [DEFAULT]" : "";
                Console.WriteLine($"{Verdict(p.Peak),-6} peak={p.Peak:F6}  {p.DeviceName}{marker}");
                Console.WriteLine($"       id={p.DeviceId}");
                foreach (var s in p.Sessions.OrderByDescending(x => x.Peak))
                    Console.WriteLine($"         APP | {s.ProcessName} (pid {s.Pid}) "
                                    + $"peak={s.Peak:F6} {Verdict(s.Peak)}");
                Console.WriteLine();
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AudioPeak] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Peak above which we call an endpoint "carrying audio".
    ///
    /// Not zero, deliberately. Always-on virtual devices (VoiceMeeter, VB-CABLE) sit at a
    /// non-zero noise floor forever, so a `peak > 0` test labels every one of them as
    /// carrying audio and prints the self-contradictory "AUDIO peak=0.0000". Anything below
    /// this is silence for our purposes; real playback is orders of magnitude above it.
    /// </summary>
    private const float AudibleThreshold = 0.0001f;

    private static string Verdict(float peak) => peak >= AudibleThreshold ? "AUDIO" : "silent";

    private static float ReadPeak(ComInterfaces.IAudioMeterInformation? meter)
    {
        if (meter is null) return 0f;
        return meter.GetPeakValue(out float peak) == 0 ? peak : 0f;
    }

    private static ComInterfaces.IAudioMeterInformation? ActivateMeter(ComInterfaces.IMMDevice device)
    {
        var iid = typeof(ComInterfaces.IAudioMeterInformation).GUID;
        return device.Activate(iid, 1, IntPtr.Zero, out var obj) == 0
            ? obj as ComInterfaces.IAudioMeterInformation
            : null;
    }

    private static string? GetFriendlyName(ComInterfaces.IMMDevice device)
    {
        if (device.OpenPropertyStore(ComInterfaces.STGM_READ, out var store) != 0) return null;
        var key = ComInterfaces.PKEY_Device_FriendlyName;
        return store.GetValue(ref key, out var pv) == 0 ? pv.GetString() : null;
    }

    private static string ProcessNameOf(int pid)
    {
        if (pid == 0) return "system sounds";
        try { return Process.GetProcessById(pid).ProcessName; }
        catch { return "(exited)"; }
    }

    private sealed class Probe
    {
        public string DeviceName = "";
        public string DeviceId = "";
        public bool IsDefault;
        public ComInterfaces.IAudioMeterInformation? Meter;
        public float Peak;
        public List<SessionProbe> Sessions = new();
    }

    private sealed class SessionProbe
    {
        public int Pid;
        public string ProcessName = "";
        public ComInterfaces.IAudioMeterInformation? Meter;
        public float Peak;
    }
}
