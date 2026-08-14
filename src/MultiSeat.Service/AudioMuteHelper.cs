using System.Diagnostics;
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
    ///
    /// A process has no audio session until it first renders audio. Under per-session audio
    /// (AudioMode.PerSession) that matters: mstsc's session does not exist at launch, and it may
    /// not exist for hours — it appears the moment the seat first plays something. A one-shot
    /// mute, or even a bounded poll, therefore misses it and lets seat audio out of the host's
    /// speakers. This was measured, not assumed: with a 120 s bound the mute expired long before
    /// a seat played anything, and the console then read mstsc at peak 0.357.
    ///
    ///   timeoutMs  = 0  → single attempt (historical behaviour, used by SharedHost).
    ///   timeoutMs  > 0  → poll until the session appears or the deadline passes.
    ///   timeoutMs  &lt; 0 → WATCH: poll for as long as the target process lives, and keep
    ///                      re-asserting the mute afterwards so a session that is torn down and
    ///                      recreated cannot come back audible. Exits when the process exits.
    ///
    /// Each attempt re-enumerates from scratch: the session list is a snapshot, and the
    /// endpoint's session collection is exactly what we are waiting to change.
    /// </summary>
    public static bool MuteByPid(int pid, int timeoutMs = 0)
    {
        return timeoutMs < 0 ? WatchAndMute(pid) : PollAndMute(pid, timeoutMs);
    }

    private static bool PollAndMute(int pid, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var attempt = 0;

        while (true)
        {
            attempt++;
            if (TryMuteOnce(pid)) return true;

            if (DateTime.UtcNow >= deadline)
            {
                Console.Error.WriteLine(
                    $"[AudioMute] No audio session for PID {pid} after {attempt} attempt(s) " +
                    $"over {timeoutMs}ms");
                return false;
            }

            Thread.Sleep(250);
        }
    }

    /// <summary>
    /// Stay alive for the target process's lifetime, muting its audio session as soon as one
    /// exists and re-asserting periodically. Cheap: one COM enumeration every few seconds.
    /// </summary>
    private static bool WatchAndMute(int pid)
    {
        // Before the first success, poll briskly — the window between a session appearing and
        // audio reaching the speakers is what we are trying to close.
        const int PollBeforeMuteMs = 1_000;
        // After it, we are only guarding against the session being recreated; be lazy.
        const int PollAfterMuteMs = 10_000;

        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine($"[AudioMute] PID {pid} is not running — nothing to watch");
            return false;
        }

        var everMuted = false;
        Console.WriteLine($"[AudioMute] Watching PID {pid} for the life of the process...");

        while (!process.HasExited)
        {
            if (TryMuteOnce(pid) && !everMuted)
            {
                everMuted = true;
                Console.WriteLine($"[AudioMute] Muted PID {pid} (watch continues until it exits)");
            }

            Thread.Sleep(everMuted ? PollAfterMuteMs : PollBeforeMuteMs);
            process.Refresh();
        }

        Console.WriteLine(
            $"[AudioMute] PID {pid} exited; watcher stopping (muted at least once: {everMuted})");
        return everMuted;
    }

    /// <summary>
    /// One enumeration pass over the default render endpoint's sessions.
    /// Returns true only if the PID's session was found and muted.
    /// </summary>
    private static bool TryMuteOnce(int pid)
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

            for (int i = 0; i < count; i++)
            {
                sessionEnum.GetSession(i, out var session);
                session.GetProcessId(out uint sessionPid);

                if (sessionPid != (uint)pid) continue;

                var vol = (ComInterfaces.ISimpleAudioVolume)session;
                var ctx = Guid.Empty;
                vol.SetMute(true, ref ctx);
                Console.WriteLine($"[AudioMute] Muted audio session for PID {pid}");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AudioMute] Exception: {ex.Message}");
            return false;
        }
    }
}
