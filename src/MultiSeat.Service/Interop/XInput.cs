using System.Runtime.InteropServices;

namespace MultiSeat.Service.Interop;

/// <summary>
/// P/Invoke for xinput1_4.dll — Xbox controller input polling.
/// Used to read physical gamepad state for forwarding to ViGEm virtual pads.
///
/// XInput supports up to 4 controllers (indices 0-3).
/// xinput1_4.dll is present on all Windows 10/11 builds.
/// </summary>
public static partial class XInput
{
    private const string Lib = "xinput1_4.dll";

    public const int XUSER_MAX_COUNT = 4;
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_DEVICE_NOT_CONNECTED = 1167;

    // ── Gamepad button flags ─────────────────────────────────────────
    [Flags]
    public enum GamepadButtons : ushort
    {
        None = 0,
        DPadUp = 0x0001,
        DPadDown = 0x0002,
        DPadLeft = 0x0004,
        DPadRight = 0x0008,
        Start = 0x0010,
        Back = 0x0020,
        LeftThumb = 0x0040,
        RightThumb = 0x0080,
        LeftShoulder = 0x0100,
        RightShoulder = 0x0200,
        A = 0x1000,
        B = 0x2000,
        X = 0x4000,
        Y = 0x8000,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Gamepad
    {
        public GamepadButtons wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct State
    {
        public uint dwPacketNumber;
        public Gamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Vibration
    {
        public ushort wLeftMotorSpeed;
        public ushort wRightMotorSpeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Capabilities
    {
        public byte Type;
        public byte SubType;
        public ushort Flags;
        public Gamepad Gamepad;
        public Vibration Vibration;
    }

    // Capability types
    public const byte XINPUT_DEVTYPE_GAMEPAD = 0x01;
    // SubTypes
    public const byte XINPUT_DEVSUBTYPE_GAMEPAD = 0x01;
    // Capability flags
    public const int XINPUT_FLAG_GAMEPAD = 0x00000001;

    // Deadzone thresholds (recommended by Microsoft)
    public const short XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE = 7849;
    public const short XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE = 8689;
    public const byte XINPUT_GAMEPAD_TRIGGER_THRESHOLD = 30;

    /// <summary>
    /// Get the current state of a controller.
    /// Returns ERROR_SUCCESS (0) if connected, ERROR_DEVICE_NOT_CONNECTED (1167) if not.
    /// </summary>
    [LibraryImport(Lib, EntryPoint = "XInputGetState")]
    public static partial int GetState(int dwUserIndex, out State pState);

    /// <summary>
    /// Set vibration on a controller.
    /// </summary>
    [LibraryImport(Lib, EntryPoint = "XInputSetState")]
    public static partial int SetState(int dwUserIndex, ref Vibration pVibration);

    /// <summary>
    /// Get the capabilities of a controller.
    /// </summary>
    [LibraryImport(Lib, EntryPoint = "XInputGetCapabilities")]
    public static partial int GetCapabilities(int dwUserIndex, int dwFlags, out Capabilities pCapabilities);
}
