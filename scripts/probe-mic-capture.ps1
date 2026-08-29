# Part of MultiSeat's diagnostic set. See also: show-audio-defaults.ps1 (which device is the
# default), audio-endpoint-volume.ps1 (how much gain it applies), and
# MultiSeat.Service.exe --audio-peaks (what is being RENDERED).
#
# This exists because --capture-loopback resolves RENDER endpoints only, so nothing we had
# could answer "would an app opening this microphone actually hear anything?".
# Capture from a RECORDING endpoint (not loopback) and report the peak level.
# Answers: would an app that opens this microphone actually hear anything?
#
# Reports frames captured as well as the peak, so "0.000000 peak" can be told apart from
# "the capture never ran" - a silent reading from a stream that produced no frames proves nothing.

param(
    [string]$Match = 'Steam Streaming Microphone',
    [double]$Seconds = 10
)

$ErrorActionPreference = 'Stop'

$src = @'
using System;
using System.Runtime.InteropServices;
using System.Threading;

public static class MicProbe {
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        int GetDevice(string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IntPtr c);
        int UnregisterEndpointNotificationCallback(IntPtr c);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection {
        int GetCount(out int count);
        int Item(int index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice {
        int Activate(ref Guid iid, int clsCtx, IntPtr ap, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        int OpenPropertyStore(int access, out IPropertyStore store);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore {
        int GetCount(out int c);
        int GetAt(int i, out PROPERTYKEY k);
        int GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
        int SetValue(ref PROPERTYKEY k, ref PROPVARIANT v);
        int Commit();
    }
    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient {
        int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        int GetBufferSize(out uint frames);
        int GetStreamLatency(out long latency);
        int GetCurrentPadding(out uint padding);
        int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        int GetMixFormat(out IntPtr format);
        int GetDevicePeriod(out long def, out long min);
        int Start();
        int Stop();
        int Reset();
        int SetEventHandle(IntPtr h);
        int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object svc);
    }
    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient {
        int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devPos, out ulong qpcPos);
        int ReleaseBuffer(uint frames);
        int GetNextPacketSize(out uint frames);
    }

    [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; }
    [StructLayout(LayoutKind.Explicit)] struct PROPVARIANT { [FieldOffset(0)] public short vt; [FieldOffset(8)] public IntPtr p; }
    [StructLayout(LayoutKind.Sequential)]
    struct WAVEFORMATEX {
        public short wFormatTag; public short nChannels; public int nSamplesPerSec;
        public int nAvgBytesPerSec; public short nBlockAlign; public short wBitsPerSample; public short cbSize;
    }

    static string NameOf(IMMDevice d) {
        IPropertyStore s;
        if (d.OpenPropertyStore(0, out s) != 0) return "(unknown)";
        PROPERTYKEY k = new PROPERTYKEY();
        k.fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"); k.pid = 14;
        PROPVARIANT v;
        if (s.GetValue(ref k, out v) != 0 || v.p == IntPtr.Zero) return "(unknown)";
        return Marshal.PtrToStringUni(v.p);
    }

    // returns: name | frames | peak | silentPackets | format
    public static string Capture(string match, double seconds) {
        Type t = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
        IMMDeviceEnumerator en = (IMMDeviceEnumerator)Activator.CreateInstance(t);

        IMMDeviceCollection col;
        int hr = en.EnumAudioEndpoints(1 /* eCapture */, 1 /* ACTIVE */, out col);
        if (hr != 0) return "ERROR|EnumAudioEndpoints 0x" + hr.ToString("X8");

        int count; col.GetCount(out count);
        IMMDevice dev = null; string name = null;
        for (int i = 0; i < count; i++) {
            IMMDevice d; col.Item(i, out d);
            string n = NameOf(d);
            if (n != null && n.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0) { dev = d; name = n; break; }
        }
        if (dev == null) return "ERROR|no active recording endpoint matching '" + match + "'";

        Guid iidClient = typeof(IAudioClient).GUID;
        object o;
        hr = dev.Activate(ref iidClient, 23 /* CLSCTX_ALL */, IntPtr.Zero, out o);
        if (hr != 0) return "ERROR|Activate(IAudioClient) 0x" + hr.ToString("X8");
        IAudioClient client = (IAudioClient)o;

        IntPtr fmt;
        hr = client.GetMixFormat(out fmt);
        if (hr != 0) return "ERROR|GetMixFormat 0x" + hr.ToString("X8");
        WAVEFORMATEX wf = (WAVEFORMATEX)Marshal.PtrToStructure(fmt, typeof(WAVEFORMATEX));

        hr = client.Initialize(0 /* shared */, 0, 10000000L, 0, fmt, IntPtr.Zero);
        if (hr != 0) return "ERROR|Initialize 0x" + hr.ToString("X8");

        Guid iidCapture = typeof(IAudioCaptureClient).GUID;
        object c2;
        hr = client.GetService(ref iidCapture, out c2);
        if (hr != 0) return "ERROR|GetService(IAudioCaptureClient) 0x" + hr.ToString("X8");
        IAudioCaptureClient cap = (IAudioCaptureClient)c2;

        client.Start();
        double peak = 0; long totalFrames = 0; int silentPackets = 0; int packets = 0;
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end) {
            Thread.Sleep(20);
            uint packetFrames;
            while (cap.GetNextPacketSize(out packetFrames) == 0 && packetFrames > 0) {
                IntPtr data; uint frames, flags; ulong dp, qp;
                if (cap.GetBuffer(out data, out frames, out flags, out dp, out qp) != 0) break;
                packets++;
                totalFrames += frames;
                if ((flags & 0x2) != 0) {           // AUDCLNT_BUFFERFLAGS_SILENT
                    silentPackets++;
                } else if (wf.wBitsPerSample == 32 && data != IntPtr.Zero) {
                    int n = (int)frames * wf.nChannels;
                    float[] buf = new float[n];
                    Marshal.Copy(data, buf, 0, n);
                    for (int i = 0; i < n; i++) { float a = Math.Abs(buf[i]); if (a > peak) peak = a; }
                } else if (wf.wBitsPerSample == 16 && data != IntPtr.Zero) {
                    int n = (int)frames * wf.nChannels;
                    short[] buf = new short[n];
                    Marshal.Copy(data, buf, 0, n);
                    for (int i = 0; i < n; i++) { double a = Math.Abs(buf[i]) / 32768.0; if (a > peak) peak = a; }
                }
                cap.ReleaseBuffer(frames);
            }
        }
        client.Stop();

        return name + "|" + totalFrames + "|" + peak.ToString("F6") + "|" + silentPackets + "/" + packets
             + "|" + wf.nChannels + "ch " + wf.nSamplesPerSec + "Hz " + wf.wBitsPerSample + "bit";
    }
}
'@

if (-not ('MicProbe' -as [type])) { Add-Type -TypeDefinition $src }

Write-Host ''
Write-Host ("Capturing {0}s from a recording endpoint matching '{1}' ..." -f $Seconds, $Match)
$r = [MicProbe]::Capture($Match, $Seconds)
$p = $r -split '\|'

if ($p[0] -eq 'ERROR') {
    Write-Host ("FAILED: {0}" -f $p[1])
    exit 2
}

Write-Host ''
Write-Host ("  device        : {0}" -f $p[0])
Write-Host ("  format        : {0}" -f $p[4])
Write-Host ("  frames        : {0}" -f $p[1])
Write-Host ("  silent packets: {0}" -f $p[3])
Write-Host ("  PEAK          : {0}" -f $p[2])
Write-Host ''

if ([long]$p[1] -eq 0) {
    Write-Host 'PROBE INVALID - the capture produced no frames at all, so a silent result means nothing.'
    exit 2
}
if ([double]$p[2] -lt 0.001) {
    Write-Host 'RESULT: SILENT - frames arrived but carried no signal (0.001 is the noise floor).'
    Write-Host 'An app opening this microphone would hear nothing.'
    exit 1
}
Write-Host 'RESULT: SIGNAL PRESENT - an app opening this microphone would hear this.'
exit 0
