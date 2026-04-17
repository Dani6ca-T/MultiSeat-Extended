using System.Runtime.InteropServices;

namespace MultiSeat.Service.Interop;

/// <summary>
/// COM interface declarations for Windows Core Audio API and
/// audio endpoint policy configuration.
///
/// Core Audio (IMMDeviceEnumerator, IMMDevice, IMMDeviceCollection, IPropertyStore)
/// is the official documented API for audio device enumeration.
///
/// IPolicyConfig is undocumented but stable across Windows 10 1803
/// through Windows 11 24H2 (build 26100+). Used by SoundSwitch,
/// AudioSwitcher, EarTrumpet, etc. to set the default audio endpoint.
///
/// Reference GUIDs sourced from:
///   - mmdeviceapi.h (Windows SDK)
///   - PolicyConfig.h (community-maintained)
///   - Tested on Windows 11 builds 22621, 22631, 26100
/// </summary>
public static class ComInterfaces
{
    // ═══════════════════════════════════════════════════════════════════
    //  CLSIDs
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>CLSID_MMDeviceEnumerator — documented, mmdeviceapi.h</summary>
    public static readonly Guid CLSID_MMDeviceEnumerator =
        new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    /// <summary>CLSID_PolicyConfigClient — undocumented, stable Win10 1803+</summary>
    public static readonly Guid CLSID_PolicyConfigClient =
        new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    // ═══════════════════════════════════════════════════════════════════
    //  IIDs
    // ═══════════════════════════════════════════════════════════════════

    public static readonly Guid IID_IMMDeviceEnumerator =
        new("A95664D2-9614-4F35-A746-DE8DB63617E6");

    public static readonly Guid IID_IPolicyConfig =
        new("F8679F50-850A-41CF-9C72-430F290290C8");

    // ═══════════════════════════════════════════════════════════════════
    //  Property Keys (PKEY)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// PKEY_Device_FriendlyName — "Speakers (Realtek Audio)", "Line 1 (Virtual Audio Cable)", etc.
    /// </summary>
    public static readonly PropertyKey PKEY_Device_FriendlyName =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    /// <summary>
    /// PKEY_DeviceInterface_FriendlyName — shorter name, e.g. "Realtek Audio", "Virtual Audio Cable"
    /// </summary>
    public static readonly PropertyKey PKEY_DeviceInterface_FriendlyName =
        new(new Guid("026E516E-B814-414B-8384-98F824C6F2C0"), 2);

    /// <summary>
    /// PKEY_AudioEndpoint_GUID — unique endpoint GUID (useful for stable identification)
    /// </summary>
    public static readonly PropertyKey PKEY_AudioEndpoint_GUID =
        new(new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 4);

    // ═══════════════════════════════════════════════════════════════════
    //  Enums
    // ═══════════════════════════════════════════════════════════════════

    public enum EDataFlow
    {
        eRender = 0,      // speakers / headphones / VAC output
        eCapture = 1,     // microphones
        eAll = 2
    }

    public enum ERole
    {
        eConsole = 0,         // system sounds, games
        eMultimedia = 1,      // music, video
        eCommunications = 2   // voice chat
    }

    /// <summary>Device state mask for IMMDeviceEnumerator.EnumAudioEndpoints</summary>
    [Flags]
    public enum DeviceState : uint
    {
        Active = 0x00000001,
        Disabled = 0x00000002,
        NotPresent = 0x00000004,
        Unplugged = 0x00000008,
        All = 0x0000000F
    }

    /// <summary>STGM_READ for IPropertyStore.Open</summary>
    public const uint STGM_READ = 0x00000000;

    // ═══════════════════════════════════════════════════════════════════
    //  Structs
    // ═══════════════════════════════════════════════════════════════════

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
        public Guid fmtid;
        public int pid;

        public PropertyKey(Guid fmtid, int pid)
        {
            this.fmtid = fmtid;
            this.pid = pid;
        }
    }

    /// <summary>
    /// PROPVARIANT — simplified for string property reads.
    /// Full PROPVARIANT is a complex union; we only need VT_LPWSTR (31).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PropVariant
    {
        public ushort vt;           // VARTYPE
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr data1;        // union: pwszVal for VT_LPWSTR
        public IntPtr data2;

        public const ushort VT_LPWSTR = 31;

        /// <summary>
        /// Read the string value if this is VT_LPWSTR, otherwise return null.
        /// </summary>
        public readonly string? GetString()
        {
            return vt == VT_LPWSTR && data1 != IntPtr.Zero
                ? Marshal.PtrToStringUni(data1)
                : null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  COM Interfaces — Core Audio API (documented)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// IMMDeviceEnumerator — entry point for enumerating audio endpoints.
    /// Documented in mmdeviceapi.h.
    /// </summary>
    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(
            EDataFlow dataFlow,
            DeviceState stateMask,
            out IMMDeviceCollection ppDevices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(
            EDataFlow dataFlow,
            ERole role,
            out IMMDevice ppEndpoint);

        [PreserveSig]
        int GetDevice(
            [MarshalAs(UnmanagedType.LPWStr)] string pwstrId,
            out IMMDevice ppDevice);

        // RegisterEndpointNotificationCallback — not needed
        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr pClient);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    /// <summary>
    /// IMMDeviceCollection — result of EnumAudioEndpoints.
    /// </summary>
    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out int pcDevices);

        [PreserveSig]
        int Item(int nDevice, out IMMDevice ppDevice);
    }

    /// <summary>
    /// IMMDevice — represents a single audio endpoint device.
    /// </summary>
    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            [MarshalAs(UnmanagedType.LPStruct)] Guid iid,
            uint dwClsCtx,
            IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

        [PreserveSig]
        int OpenPropertyStore(
            uint stgmAccess,
            out IPropertyStore ppProperties);

        [PreserveSig]
        int GetId(
            [MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

        [PreserveSig]
        int GetState(out DeviceState pdwState);
    }

    /// <summary>
    /// IPropertyStore — access device properties (name, GUID, etc.)
    /// </summary>
    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out int cProps);

        [PreserveSig]
        int GetAt(int iProp, out PropertyKey pkey);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant pv);

        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant propvar);

        [PreserveSig]
        int Commit();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  COM Interfaces — Audio Session API (documented, audiopolicy.h)
    //  Used to mute a specific process's audio session by PID.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// IAudioSessionManager2 — access per-session audio controls.
    /// vtable: (IUnknown) + (IAudioSessionManager: GetAudioSessionControl, GetSimpleAudioVolume)
    ///         + GetSessionEnumerator, RegisterSessionNotification, UnregisterSessionNotification,
    ///           RegisterDuckNotification, UnregisterDuckNotification
    /// </summary>
    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionManager2
    {
        // IAudioSessionManager base methods (must appear first in vtable)
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint streamFlags, out IntPtr sessionControl);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint streamFlags, out IntPtr audioVolume);
        // IAudioSessionManager2 methods
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
        [PreserveSig] int RegisterSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterSessionNotification(IntPtr notification);
        [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr notification);
        [PreserveSig] int UnregisterDuckNotification(IntPtr notification);
    }

    /// <summary>
    /// IAudioSessionEnumerator — iterates the audio sessions on an endpoint.
    /// </summary>
    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    /// <summary>
    /// IAudioSessionControl2 — per-session control including GetProcessId.
    /// vtable: (IAudioSessionControl: 9 methods) + (IAudioSessionControl2: 5 methods)
    /// </summary>
    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionControl2
    {
        // IAudioSessionControl base methods
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid eventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingParam);
        [PreserveSig] int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notifications);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notifications);
        // IAudioSessionControl2 methods
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string instanceId);
        [PreserveSig] int GetProcessId(out uint processId);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    /// <summary>
    /// ISimpleAudioVolume — per-session mute and volume control.
    /// The same COM object that implements IAudioSessionControl2 also implements
    /// this interface; cast via Marshal.QueryInterface.
    /// </summary>
    [ComImport]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  COM Interface — IPolicyConfig (undocumented, stable)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// IPolicyConfig — undocumented COM interface for setting the default
    /// audio endpoint. The vtable layout has been reverse-engineered and
    /// validated across Windows 10 1803 → Windows 11 24H2.
    ///
    /// The critical method is SetDefaultEndpoint (vtable index 10),
    /// which sets the default audio output device for a given ERole.
    ///
    /// Note: This interface operates on the calling process's audio session.
    /// For per-session audio routing, we must call it from WITHIN the target
    /// session (via ProcessInjector) or use a helper process launched in the
    /// target session that sets the default device on startup.
    /// </summary>
    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPolicyConfig
    {
        // vtable slots 0-9: GetMixFormat, GetDeviceFormat, ResetDeviceFormat,
        // SetDeviceFormat, GetProcessingPeriod, SetProcessingPeriod,
        // GetShareMode, SetShareMode, GetPropertyValue, SetPropertyValue
        void Reserved1();
        void Reserved2();
        void Reserved3();
        void Reserved4();
        void Reserved5();
        void Reserved6();
        void Reserved7();
        void Reserved8();
        void Reserved9();
        void Reserved10();

        /// <summary>
        /// SetDefaultEndpoint — vtable slot 10.
        /// Sets the default audio endpoint for the specified role.
        /// </summary>
        /// <param name="deviceId">
        /// The endpoint device ID string from IMMDevice.GetId().
        /// Example: "{0.0.0.00000000}.{GUID}"
        /// </param>
        /// <param name="role">0 = eConsole, 1 = eMultimedia, 2 = eCommunications</param>
        [PreserveSig]
        int SetDefaultEndpoint(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            int role);
    }
}
