# Part of MultiSeat's diagnostic set. Windows keeps THREE defaults per direction (Console,
# Multimedia, Communications) and voice chat follows Communications - so "the default mic"
# is an ambiguous question until you look at all three.
# Report the default audio endpoints per role, for capture and render, via the MMDevice API.
# Read-only. Roles: 0 = eConsole, 1 = eMultimedia, 2 = eCommunications.
# All COM work happens inside C# - PowerShell only ever sees strings, because a raw __ComObject
# gets late-bound by PS and these interfaces are not IDispatch.

$ErrorActionPreference = 'Stop'

$src = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class MMD {
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        int GetDevice(string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr iface);
        int OpenPropertyStore(int access, out IPropertyStore store);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore {
        int GetCount(out int count);
        int GetAt(int index, out PROPERTYKEY key);
        int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPERTYKEY { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Explicit)]
    struct PROPVARIANT {
        [FieldOffset(0)] public short vt;
        [FieldOffset(8)] public IntPtr p;
    }

    static string NameOf(IMMDevice dev) {
        IPropertyStore store;
        if (dev.OpenPropertyStore(0, out store) != 0) return "(property store unavailable)";
        PROPERTYKEY key = new PROPERTYKEY();
        key.fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");  // PKEY_Device_FriendlyName
        key.pid = 14;
        PROPVARIANT v;
        if (store.GetValue(ref key, out v) != 0) return "(name unavailable)";
        if (v.p == IntPtr.Zero) return "(no name)";
        return Marshal.PtrToStringUni(v.p);
    }

    // "flow|role|name|id" per line; flow 1 = capture, 0 = render.
    public static string[] Defaults() {
        Type t = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
        IMMDeviceEnumerator en = (IMMDeviceEnumerator)Activator.CreateInstance(t);
        List<string> outp = new List<string>();
        int[] flows = new int[] { 1, 0 };
        int[] roles = new int[] { 0, 1, 2 };
        foreach (int f in flows) {
            foreach (int r in roles) {
                IMMDevice dev;
                int hr = en.GetDefaultAudioEndpoint(f, r, out dev);
                if (hr != 0 || dev == null) {
                    outp.Add(f + "|" + r + "|(none, hr=0x" + hr.ToString("X8") + ")|");
                    continue;
                }
                string id;
                dev.GetId(out id);
                outp.Add(f + "|" + r + "|" + NameOf(dev) + "|" + id);
            }
        }
        return outp.ToArray();
    }
}
'@

if (-not ('MMD' -as [type])) { Add-Type -TypeDefinition $src }

$roleName = @{ '0' = 'Console'; '1' = 'Multimedia'; '2' = 'Communications' }
$flowName = @{ '1' = 'CAPTURE (recording)'; '0' = 'RENDER (playback)' }

$lastFlow = ''
foreach ($line in [MMD]::Defaults()) {
    $parts = $line -split '\|', 4
    if ($parts[0] -ne $lastFlow) {
        Write-Host ''
        Write-Host ("== default {0} endpoints ==" -f $flowName[$parts[0]])
        $lastFlow = $parts[0]
    }
    Write-Host ("  {0,-15} : {1}" -f $roleName[$parts[1]], $parts[2])
    if ($parts[3]) { Write-Host ("  {0,-15}   {1}" -f '', $parts[3]) }
}
Write-Host ''
