# Part of MultiSeat's diagnostic set. Note a capture endpoint's slider can sit ABOVE 0 dB:
# 0.750 scalar measured +7.7 dB on this host and was clipping speech. -SetDb 0 is unity.
#
# WARNING for anyone extending the interop below: IAudioEndpointVolume.GetVolumeRange is
# vtable slot 18, not 8. Declaring it after GetMasterVolumeLevelScalar makes the call land
# on SetChannelVolumeLevel instead - which here failed loudly, but a wrong slot that
# succeeds would silently rewrite the user's levels. Declare every preceding method.
# Read (and optionally set) the master volume of audio endpoints matching a name.
# Read-only unless -SetScalar or -SetDb is given.
#   -Flow 0 = render only, 1 = capture only, -1 = both (default)
#   -SetDb 0  sets exactly unity gain (no boost, no attenuation)

param(
    [string]$Match = 'Steam Streaming Microphone',
    [int]$Flow = -1,
    [double]$SetScalar = -1,
    [double]$SetDb = -999
)

$ErrorActionPreference = 'Stop'

$src = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class EpVol {
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        int GetDevice(string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IntPtr c);
        int UnregisterEndpointNotificationCallback(IntPtr c);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection { int GetCount(out int c); int Item(int i, out IMMDevice d); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice {
        int Activate(ref Guid iid, int clsCtx, IntPtr ap, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        int OpenPropertyStore(int access, out IPropertyStore store);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore {
        int GetCount(out int c); int GetAt(int i, out PROPERTYKEY k);
        int GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
        int SetValue(ref PROPERTYKEY k, ref PROPVARIANT v); int Commit();
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume {
        int RegisterControlChangeNotify(IntPtr n);
        int UnregisterControlChangeNotify(IntPtr n);
        int GetChannelCount(out uint c);
        int SetMasterVolumeLevel(float level, IntPtr ev);
        int SetMasterVolumeLevelScalar(float level, IntPtr ev);
        int GetMasterVolumeLevel(out float level);
        int GetMasterVolumeLevelScalar(out float level);
    }
    [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; }
    [StructLayout(LayoutKind.Explicit)] struct PROPVARIANT { [FieldOffset(0)] public short vt; [FieldOffset(8)] public IntPtr p; }

    static string NameOf(IMMDevice d) {
        IPropertyStore s;
        if (d.OpenPropertyStore(0, out s) != 0) return "(unknown)";
        PROPERTYKEY k = new PROPERTYKEY();
        k.fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"); k.pid = 14;
        PROPVARIANT v;
        if (s.GetValue(ref k, out v) != 0 || v.p == IntPtr.Zero) return "(unknown)";
        return Marshal.PtrToStringUni(v.p);
    }

    // flow|name|scalar|dB|rangeMin..rangeMax
    public static string[] Report(string match, int flowFilter, double setScalar, double setDb) {
        Type t = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
        IMMDeviceEnumerator en = (IMMDeviceEnumerator)Activator.CreateInstance(t);
        List<string> outp = new List<string>();
        Guid iid = typeof(IAudioEndpointVolume).GUID;

        foreach (int flow in new int[] { 0, 1 }) {
            if (flowFilter >= 0 && flow != flowFilter) continue;
            IMMDeviceCollection col;
            if (en.EnumAudioEndpoints(flow, 1, out col) != 0) continue;
            int count; col.GetCount(out count);
            for (int i = 0; i < count; i++) {
                IMMDevice d; col.Item(i, out d);
                string n = NameOf(d);
                if (n == null || n.IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0) continue;
                object o;
                if (d.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) { outp.Add(flow + "|" + n + "|(no volume control)||"); continue; }
                IAudioEndpointVolume vol = (IAudioEndpointVolume)o;
                if (setScalar >= 0) vol.SetMasterVolumeLevelScalar((float)setScalar, IntPtr.Zero);
                if (setDb > -900) vol.SetMasterVolumeLevel((float)setDb, IntPtr.Zero);
                float sc, db;
                vol.GetMasterVolumeLevelScalar(out sc);
                vol.GetMasterVolumeLevel(out db);
                outp.Add(flow + "|" + n + "|" + sc.ToString("F3") + "|" + db.ToString("F1") + "|");
            }
        }
        return outp.ToArray();
    }
}
'@

if (-not ('EpVol' -as [type])) { Add-Type -TypeDefinition $src }

$flowName = @{ '0' = 'RENDER '; '1' = 'CAPTURE' }
Write-Host ''
foreach ($line in [EpVol]::Report($Match, $Flow, $SetScalar, $SetDb)) {
    $p = $line -split '\|'
    Write-Host ("  [{0}] {1,-45} volume {2}  ({3} dB)" -f $flowName[$p[0]], $p[1], $p[2], $p[3])
}
Write-Host ''
