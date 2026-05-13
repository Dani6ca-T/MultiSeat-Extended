# Show Apollo configs
$apolloDir = 'C:\ProgramData\MultiSeat\apollo'
$configs = Get-ChildItem $apolloDir -Filter 'sunshine.conf' -Recurse -EA SilentlyContinue
if ($configs) {
    foreach ($f in $configs) {
        Write-Host "=== $($f.FullName) ==="
        Get-Content $f.FullName | Where-Object { $_ -match 'virtual_sink|audio|sink|mic' }
    }
} else {
    Write-Host "No sunshine.conf files found (no seats provisioned)"
}

Write-Host "`n=== Active render endpoints (from SYSTEM/Session 0) ==="
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumerator {}
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator {
    int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection ppDevices);
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
    int GetDevice(string pwstrId, out IMMDevice ppDevice);
    int RegisterEndpointNotificationCallback(IntPtr pClient);
    int UnregisterEndpointNotificationCallback(IntPtr pClient);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection {
    int GetCount(out uint pcDevices);
    int Item(uint nDevice, out IMMDevice ppDevice);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice {
    int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);
    int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
    int GetState(out int pdwState);
}
[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPropertyStore {
    int GetCount(out uint cProps);
    int GetAt(uint iProp, out System.Runtime.InteropServices.ComTypes.PROPSPEC pspec);
    int GetValue(ref System.Runtime.InteropServices.ComTypes.PROPSPEC prop, out System.Runtime.InteropServices.ComTypes.PROPVARIANT pv);
    int SetValue(ref System.Runtime.InteropServices.ComTypes.PROPSPEC prop, ref System.Runtime.InteropServices.ComTypes.PROPVARIANT pv);
    int Commit();
}
'@ -EA SilentlyContinue

# Simpler: use wmic or PowerShell audio cmdlets
Get-WmiObject Win32_SoundDevice | Select-Object Name, Status | Format-Table -AutoSize

Write-Host "`n=== Event log: audio-related warnings ==="
Get-EventLog Application -Source 'MultiSeat*' -Newest 5 -EA SilentlyContinue |
    Where-Object { $_.Message -match 'audio|capture|mic|VAC|cable' } |
    ForEach-Object { Write-Host $_.Message }
