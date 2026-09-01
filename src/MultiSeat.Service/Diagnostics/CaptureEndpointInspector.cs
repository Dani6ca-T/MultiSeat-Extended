using Microsoft.Extensions.Logging.Abstractions;
using MultiSeat.Service.Audio;
using MultiSeat.Service.Interop;

namespace MultiSeat.Service.Diagnostics;

/// <summary>
/// Lists the audio CAPTURE endpoints visible from the session this runs in, and says whether the
/// Steam Streaming Microphone — the device Apollo's stream_mic path depends on — is among them.
///
/// WHY THIS EXISTS
///
/// `--audio-peaks` enumerates render endpoints only, so nothing here could answer the capture
/// question. And the capture question is the one holding up a real decision: PerSession audio is
/// documented as costing the microphone, on the grounds that "a session that keeps its own audio
/// cannot see the host's Steam Streaming Microphone".
///
/// That is an inference, not a measurement. The measurement we actually have (14 endpoints under
/// audiomode:i:1, 0 under i:2) is about RENDER endpoints, and `audiomode` governs playback
/// redirection. Capture enumeration is a different axis — `audiocapturemode` — which MultiSeat
/// deliberately leaves unset. Whether that hides the host's capture devices from the session is
/// not something the render measurement establishes either way.
///
/// So: ask the session. Endpoint enumeration is session-scoped, which is precisely why this has to
/// run INSIDE a seat rather than in the service (session 0 sees nothing). Launch it in a seat
/// session with a scheduled task using -LogonType Interactive, which needs no password.
///
/// Exit code 0 = the Steam Streaming Microphone capture endpoint is visible from here, so a
/// microphone path is possible in this session; 1 = it is not; 2 = enumeration itself failed, so
/// the run says nothing either way.
///
/// Usage: MultiSeat.Service.exe --list-capture
/// </summary>
public static class CaptureEndpointInspector
{
    /// <summary>The capture endpoint Apollo's stream_mic path feeds games from.</summary>
    internal const string SteamMicName = "Steam Streaming Microphone";

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine("== audio capture endpoints visible from this session ==");
        Console.WriteLine($"  session: {Environment.GetEnvironmentVariable("SESSIONNAME") ?? "(unknown)"}");
        Console.WriteLine();

        List<AudioEndpointInfo> active;
        List<AudioEndpointInfo> all;
        try
        {
            var enumerator = new AudioDeviceEnumerator(NullLogger<AudioDeviceEnumerator>.Instance);
            active = enumerator.EnumerateCaptureEndpoints(ComInterfaces.DeviceState.Active);

            // Also read every state. A device that is present but DISABLED or UNPLUGGED is a
            // completely different diagnosis from one the session cannot see at all, and the
            // active-only list cannot tell those apart.
            all = enumerator.EnumerateCaptureEndpoints(ComInterfaces.DeviceState.All);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  enumeration failed: {ex.Message}");
            Console.Error.WriteLine("  This run establishes nothing — do not read it as 'no devices'.");
            return 2;
        }

        if (active.Count == 0 && all.Count == 0)
        {
            Console.WriteLine("  (none — not one capture endpoint in any state)");
        }

        foreach (var d in active)
            Console.WriteLine($"  ACTIVE    {d.FriendlyName}");

        foreach (var d in all.Where(d => active.All(a => a.DeviceId != d.DeviceId)))
            Console.WriteLine($"  inactive  {d.FriendlyName}   (present but not active)");

        Console.WriteLine();
        Console.WriteLine($"  active: {active.Count}    all states: {all.Count}");

        Console.WriteLine();
        Console.WriteLine("== the device stream_mic needs ==");

        var steamActive = active.FirstOrDefault(d => IsSteamMic(d.FriendlyName));
        var steamAny = all.FirstOrDefault(d => IsSteamMic(d.FriendlyName));

        if (steamActive is not null)
        {
            Console.WriteLine($"  VISIBLE and ACTIVE: {steamActive.FriendlyName}");
            Console.WriteLine();
            Console.WriteLine("  A microphone path is possible from this session. If AudioMode is");
            Console.WriteLine("  PerSession, the documented 'no microphone' cost is wrong and worth");
            Console.WriteLine("  retesting end to end before anyone is told to use SharedHost for it.");
            return 0;
        }

        if (steamAny is not null)
        {
            Console.WriteLine($"  PRESENT BUT NOT ACTIVE: {steamAny.FriendlyName}");
            Console.WriteLine();
            Console.WriteLine("  The session can see the device, so this is not a visibility problem.");
            Console.WriteLine("  Check whether Steam is running — the endpoint goes inactive without it.");
            return 1;
        }

        Console.WriteLine($"  NOT VISIBLE: no capture endpoint named '{SteamMicName}'.");
        Console.WriteLine();
        Console.WriteLine("  Before concluding the session hides it, confirm the device exists on the");
        Console.WriteLine("  HOST at all — if Steam is not installed there is nothing to see from");
        Console.WriteLine("  anywhere, and that is a different fault with a different fix.");
        return 1;
    }

    internal static bool IsSteamMic(string friendlyName) =>
        friendlyName.Contains(SteamMicName, StringComparison.OrdinalIgnoreCase);
}
