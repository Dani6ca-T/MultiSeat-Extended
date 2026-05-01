using System.Runtime.InteropServices;
using System.Security.Principal;
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
    private static readonly string LogPath = @"C:\ProgramData\MultiSeat\logs\audio-helper.log";

    /// <summary>
    /// Set a Windows default audio endpoint for the current session.
    /// Works for both render (output) and capture (input) device IDs —
    /// IPolicyConfig.SetDefaultEndpoint infers direction from the device.
    /// Applies to all three ERole values (eConsole, eMultimedia, eCommunications).
    /// Returns true on success.
    /// </summary>
    public static bool SetDefaultAudioDevice(string deviceId)
    {
        var sessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        var user = WindowsIdentity.GetCurrent().Name;
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);

            var type = Type.GetTypeFromCLSID(ComInterfaces.CLSID_PolicyConfigClient, throwOnError: true)!;
            var obj = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Failed to create PolicyConfigClient");

            var policy = (ComInterfaces.IPolicyConfig)obj;

            var logLines = new List<string>
            {
                $"[{timestamp}] SetDefaultAudioDevice deviceId={deviceId}",
                $"  SessionId={sessionId} User={user}",
            };

            bool allOk = true;
            try
            {
                // Set as default for all three roles: eConsole=0, eMultimedia=1, eCommunications=2
                for (int role = 0; role < 3; role++)
                {
                    var hr = policy.SetDefaultEndpoint(deviceId, role);
                    logLines.Add($"  role={role} HRESULT=0x{hr:X8} {(hr == 0 ? "OK" : "FAILED")}");
                    if (hr != 0)
                    {
                        Console.Error.WriteLine($"SetDefaultEndpoint role={role} HRESULT 0x{hr:X8}");
                        allOk = false;
                    }
                }

                logLines.Add($"  Result={(allOk ? "SUCCESS" : "PARTIAL_FAILURE")}");
                Console.WriteLine($"Default audio device set: {deviceId} (session {sessionId}, user {user})");
            }
            finally
            {
                Marshal.ReleaseComObject(obj);
                File.AppendAllLines(LogPath, logLines);
            }

            return allOk;
        }
        catch (Exception ex)
        {
            var msg = $"[{timestamp}] SetDefaultAudioDevice EXCEPTION sessionId={sessionId} user={user} deviceId={deviceId}: {ex.Message}";
            Console.Error.WriteLine(msg);
            try { File.AppendAllText(LogPath, msg + Environment.NewLine); } catch { }
            return false;
        }
    }
}
