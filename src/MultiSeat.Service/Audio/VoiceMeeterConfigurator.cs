using System.Runtime.InteropServices;

namespace MultiSeat.Service.Audio;

/// <summary>
/// Configures VoiceMeeter Potato routing via the VoiceMeeter Remote API.
///
/// The only thing MultiSeat needs to guarantee is that B1 is enabled on each
/// virtual input strip used for mic rendering (Strip[3], [4], [5]).
///
/// Without B1, audio rendered to "VoiceMeeter Input" by Apollo (virtual_sink)
/// reaches VoiceMeeter's mixer but never flows to the B1 bus, so the
/// "VoiceMeeter Output" capture device (which captures the B1 mix) stays
/// silent — the microphone appears to not work in games.
///
/// Called once after VoiceMeeter starts, before audio device enumeration.
/// </summary>
public static class VoiceMeeterConfigurator
{
    private const string VmDll = @"C:\Program Files\VB\Voicemeeter\VoicemeeterRemote64.dll";

    // VoiceMeeter Potato layout:
    //   Strip[0..2] = 3 hardware inputs
    //   Strip[3]    = VoiceMeeter Input   (VAIO1) — mic render for seat 0
    //   Strip[4]    = VoiceMeeter Aux Input (VAIO2) — game render for seat 1 (no mic needed)
    //   Strip[5]    = VoiceMeeter VAIO3 Input       — mic render for seat 1
    // We set B1=1 on all three virtual strips; harmless if a strip is unused.
    private static readonly int[] VirtualInputStrips = [3, 4, 5];

    /// <summary>
    /// Ensure B1 routing is enabled on all VoiceMeeter virtual input strips.
    /// No-op if VoiceMeeter's Remote API DLL is not present.
    /// Logs a warning but never throws — audio routing failure is non-fatal at this point.
    /// </summary>
    public static void EnsureMicRouting(ILogger logger)
    {
        if (!File.Exists(VmDll))
        {
            logger.LogDebug("VoiceMeeter Remote API not found at expected path — skipping B1 config");
            return;
        }

        try
        {
            var loginResult = VBVMR_Login();
            if (loginResult < 0)
            {
                logger.LogWarning(
                    "VoiceMeeter Remote API login failed (result {R}) — " +
                    "B1 mic routing not configured automatically. " +
                    "Enable B1 on each VAIO strip in VoiceMeeter manually.", loginResult);
                return;
            }

            // Drain any pending parameter-changed flag before writing
            VBVMR_IsParametersDirty();

            foreach (var strip in VirtualInputStrips)
            {
                var param = $"Strip[{strip}].B1";
                var result = VBVMR_SetParameterFloat(param, 1.0f);
                if (result == 0)
                    logger.LogInformation("VoiceMeeter: {Param} = 1 (B1 enabled)", param);
                else
                    logger.LogWarning("VoiceMeeter: failed to set {Param} (result {R})", param, result);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "VoiceMeeter B1 routing configuration failed (non-critical)");
        }
        finally
        {
            try { VBVMR_Logout(); } catch { /* best effort */ }
        }
    }

    [DllImport(VmDll, EntryPoint = "VBVMR_Login")]
    private static extern int VBVMR_Login();

    [DllImport(VmDll, EntryPoint = "VBVMR_Logout")]
    private static extern int VBVMR_Logout();

    [DllImport(VmDll, EntryPoint = "VBVMR_IsParametersDirty")]
    private static extern int VBVMR_IsParametersDirty();

    [DllImport(VmDll, EntryPoint = "VBVMR_SetParameterFloat", CharSet = CharSet.Ansi)]
    private static extern int VBVMR_SetParameterFloat(
        [MarshalAs(UnmanagedType.LPStr)] string paramName, float value);
}
