using MultiSeat.Service.Interop;

namespace MultiSeat.Service;

/// <summary>
/// Mutes a process's audio session via the Windows Core Audio API.
///
/// Must run IN the target Windows session (Session 1 / console session) because
/// IAudioSessionManager2::GetSessionEnumerator() is session-scoped — a SYSTEM
/// service in Session 0 only sees Session 0 audio sessions, not the console user's.
///
/// Invoked by the service via RunInConsoleSession (CreateProcessAsUser) so the
/// process runs in Session 1 where mstsc's audio session is visible.
/// </summary>
internal static class AudioMuteHelper
{
    /// <summary>
    /// Mute the audio session belonging to the given PID.
    /// Returns true if the session was found and muted.
    /// </summary>
    public static bool MuteByPid(int pid)
    {
        try
        {
            var type = Type.GetTypeFromCLSID(ComInterfaces.CLSID_MMDeviceEnumerator, throwOnError: false);
            if (type is null)
            {
                Console.Error.WriteLine($"[AudioMute] MMDeviceEnumerator not available");
                return false;
            }

            var enumerator = (ComInterfaces.IMMDeviceEnumerator)Activator.CreateInstance(type)!;
            int hr = enumerator.GetDefaultAudioEndpoint(
                ComInterfaces.EDataFlow.eRender, ComInterfaces.ERole.eConsole, out var device);
            if (hr != 0)
            {
                Console.Error.WriteLine($"[AudioMute] GetDefaultAudioEndpoint hr=0x{hr:X8}");
                return false;
            }

            var iid = typeof(ComInterfaces.IAudioSessionManager2).GUID;
            hr = device.Activate(iid, 1, IntPtr.Zero, out var sessionMgrObj);
            if (hr != 0)
            {
                Console.Error.WriteLine($"[AudioMute] Activate(IAudioSessionManager2) hr=0x{hr:X8}");
                return false;
            }

            var mgr = (ComInterfaces.IAudioSessionManager2)sessionMgrObj;
            hr = mgr.GetSessionEnumerator(out var sessionEnum);
            if (hr != 0)
            {
                Console.Error.WriteLine($"[AudioMute] GetSessionEnumerator hr=0x{hr:X8}");
                return false;
            }

            sessionEnum.GetCount(out int count);
            Console.WriteLine($"[AudioMute] {count} audio session(s) found in this session");

            for (int i = 0; i < count; i++)
            {
                sessionEnum.GetSession(i, out var session);
                session.GetProcessId(out uint sessionPid);
                Console.WriteLine($"[AudioMute]   Session {i}: PID={sessionPid}");

                if (sessionPid != (uint)pid) continue;

                var vol = (ComInterfaces.ISimpleAudioVolume)session;
                var ctx = Guid.Empty;
                vol.SetMute(true, ref ctx);
                Console.WriteLine($"[AudioMute] Muted audio session for PID {pid}");
                return true;
            }

            Console.Error.WriteLine($"[AudioMute] No audio session found for PID {pid}");
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AudioMute] Exception: {ex.Message}");
            return false;
        }
    }
}
