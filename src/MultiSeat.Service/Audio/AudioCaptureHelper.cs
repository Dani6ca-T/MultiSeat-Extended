using System.Runtime.InteropServices;
using MultiSeat.Service.Interop;

namespace MultiSeat.Service.Audio;

/// <summary>
/// Sets the default audio capture (microphone) device for the current Windows session.
///
/// Must be invoked as a helper process running INSIDE the target seat's RDP session
/// so that IPolicyConfig.SetDefaultEndpoint affects that session's audio policy only.
/// The service launches this via SessionLauncher.RunHelperInSeatSession
/// with the --set-default-capture flag.
///
/// This makes "CABLE Output" (or the VoiceMeeter equivalent) the default mic in the
/// seat's session so games automatically receive Moonlight-forwarded microphone audio
/// without any manual device selection.
/// </summary>
public static class AudioCaptureHelper
{
    /// <summary>
    /// Set a Windows default audio endpoint for the current session.
    /// Works for both render (output) and capture (input) device IDs —
    /// IPolicyConfig.SetDefaultEndpoint infers direction from the device.
    /// Applies to all three ERole values (eConsole, eMultimedia, eCommunications).
    /// Returns true on success.
    /// </summary>
    public static bool SetDefaultAudioDevice(string deviceId)
    {
        try
        {
            var type = Type.GetTypeFromCLSID(ComInterfaces.CLSID_PolicyConfigClient, throwOnError: true)!;
            var obj = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Failed to create PolicyConfigClient");

            var policy = (ComInterfaces.IPolicyConfig)obj;

            try
            {
                // Set as default for all three roles: eConsole=0, eMultimedia=1, eCommunications=2
                for (int role = 0; role < 3; role++)
                {
                    var hr = policy.SetDefaultEndpoint(deviceId, role);
                    if (hr != 0)
                        Console.Error.WriteLine($"SetDefaultEndpoint role={role} HRESULT 0x{hr:X8}");
                }

                Console.WriteLine($"Default capture device set: {deviceId}");
                return true;
            }
            finally
            {
                Marshal.ReleaseComObject(obj);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SetDefaultCaptureDevice failed: {ex.Message}");
            return false;
        }
    }
}
