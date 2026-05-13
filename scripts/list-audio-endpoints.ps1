Add-Type -AssemblyName System.Runtime.InteropServices -EA SilentlyContinue
$code = @'
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;

public static class AudioEnum {
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDevEnum {}
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevEnum {
        int EnumAudioEndpoints(int flow, int state, out IMMDevCol col);
        int GetDefault(int flow, int role, out IMMDev dev);
        int GetDevice(string id, out IMMDev dev); int Reg(IntPtr cb); int Unreg(IntPtr cb);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevCol { int GetCount(out uint n); int Item(uint i, out IMMDev dev); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDev {
        int Activate(ref Guid id, int ctx, IntPtr p, out IntPtr iface);
        int OpenProps(uint access, out IPropStore ps);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropStore {
        int GetCount(out uint n); int GetAt(uint i, out Guid k);
        int GetValue(ref Guid k, out PropVar v); int SetValue(ref Guid k, ref PropVar v); int Commit();
    }
    [StructLayout(LayoutKind.Sequential)] public struct PropVar {
        public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2;
        public string Str() => vt == 31 ? Marshal.PtrToStringUni(p) ?? "" : "";
    }
    static Guid PKEY_FriendlyName = new Guid("{a45c254e-df1c-4efd-8020-67d146a850e0}");
    const int PROP_ID_FriendlyName = 14;
    public static List<string[]> List(int flow) {
        var r = new List<string[]>();
        var e = (IMMDevEnum)new MMDevEnum();
        e.EnumAudioEndpoints(flow, 1, out var col); col.GetCount(out var n);
        for (uint i = 0; i < n; i++) {
            col.Item(i, out var dev); dev.GetId(out var id); dev.GetState(out var st);
            dev.OpenProps(0, out var ps);
            var k = PKEY_FriendlyName; int ki = PROP_ID_FriendlyName;
            ps.GetValue(ref k, out var pv);
            r.Add(new[]{ id, pv.Str() });
            if (pv.p != IntPtr.Zero) Marshal.FreeCoTaskMem(pv.p);
        }
        return r;
    }
}
'@
Add-Type -TypeDefinition $code -EA SilentlyContinue

Write-Host "=== Render endpoints ==="
try {
    [AudioEnum]::List(0) | ForEach-Object { Write-Host "  $($_[1])`n    ID: $($_[0])" }
} catch { Write-Host "COM enumeration failed: $_" }

Write-Host "`n=== Capture endpoints ==="
try {
    [AudioEnum]::List(1) | ForEach-Object { Write-Host "  $($_[1])`n    ID: $($_[0])" }
} catch { Write-Host "COM enumeration failed: $_" }
