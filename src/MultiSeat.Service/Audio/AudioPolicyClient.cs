using System.Runtime.InteropServices;
using MultiSeat.Service.Interop;

namespace MultiSeat.Service.Audio;

/// <summary>
/// Wraps the undocumented IPolicyConfig COM interface to programmatically
/// set the default audio endpoint device.
///
/// CRITICAL: IPolicyConfig.SetDefaultEndpoint() changes the default device
/// for the ENTIRE SYSTEM, not per-session. Windows does not have a built-in
/// per-session audio policy API.
///
/// For per-session audio isolation, we use one of two strategies:
///
/// Strategy A (preferred): Launch a helper process INSIDE the target session
///   that calls IPolicyConfig.SetDefaultEndpoint() for that session's desktop.
///   Since each session has its own audio graph and default device registry,
///   this only affects audio within that session. This works because Windows
///   maintains separate audio policy state per-session (stored in the user's
///   HKCU\Software\Microsoft\Multimedia\Audio).
///
/// Strategy B (fallback): For sessions that share the same audio policy
///   (e.g., session 0 services), use Apollo's audio_sink config to capture
///   from a specific device rather than relying on the default.
///
/// This class handles both:
///   - Direct invocation (for calling from within a target session)
///   - Remote invocation via ProcessInjector (launches a helper in the session)
///
/// Tested on Windows 10 1803 → Windows 11 24H2 (build 26100).
/// </summary>
public sealed class AudioPolicyClient : IDisposable
{
    private ComInterfaces.IPolicyConfig? _policyConfig;

    public AudioPolicyClient()
    {
        var hr = CoCreateInstance(
            ComInterfaces.CLSID_PolicyConfigClient,
            IntPtr.Zero,
            1, // CLSCTX_INPROC_SERVER
            ComInterfaces.IID_IPolicyConfig,
            out var obj);

        if (hr != 0)
            throw new COMException(
                "Failed to create PolicyConfigClient. " +
                "This COM object must be created within an interactive session.", hr);

        _policyConfig = (ComInterfaces.IPolicyConfig)obj;
    }

    /// <summary>
    /// Set the default audio output device for ALL roles
    /// (Console, Multimedia, Communications).
    /// Only affects the session this process is running in.
    /// </summary>
    public void SetDefaultEndpoint(string deviceId)
    {
        if (_policyConfig is null)
            throw new ObjectDisposedException(nameof(AudioPolicyClient));

        for (int role = 0; role < 3; role++)
        {
            var hr = _policyConfig.SetDefaultEndpoint(deviceId, role);
            if (hr != 0)
                Marshal.ThrowExceptionForHR(hr);
        }
    }

    /// <summary>
    /// Set the default audio output device for a specific role only.
    /// </summary>
    public void SetDefaultEndpoint(string deviceId, ComInterfaces.ERole role)
    {
        if (_policyConfig is null)
            throw new ObjectDisposedException(nameof(AudioPolicyClient));

        var hr = _policyConfig.SetDefaultEndpoint(deviceId, (int)role);
        if (hr != 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    public void Dispose()
    {
        if (_policyConfig is not null)
        {
            Marshal.ReleaseComObject(_policyConfig);
            _policyConfig = null;
        }
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
}

/// <summary>
/// Generates a small C# script or compiled helper that is launched
/// inside the target session to set the default audio device.
///
/// The helper is a minimal console app:
///   MultiSeatAudioHelper.exe --device-id "{0.0.0.00000000}.{guid}"
///
/// It creates an AudioPolicyClient, calls SetDefaultEndpoint, and exits.
/// This ensures the IPolicyConfig call happens within the session's
/// audio context.
/// </summary>
public static class AudioSessionHelper
{
    /// <summary>
    /// Build the command line for the audio helper process.
    /// The helper executable is deployed alongside the MultiSeat service.
    /// </summary>
    public static string BuildHelperCommandLine(string deviceId)
    {
        // The helper is a separate minimal executable that:
        //   1. Creates AudioPolicyClient
        //   2. Calls SetDefaultEndpoint(deviceId) for all roles
        //   3. Exits with code 0 on success, 1 on failure
        //
        // We use the MultiSeat service's own assembly to avoid deploying
        // a separate binary. The service can be invoked with a special
        // --set-audio flag when launched as a one-shot helper:
        return $"\"{{TRIO_EXE}}\" --set-audio \"{deviceId}\"";
    }

    /// <summary>
    /// Process the --set-audio command when the service is invoked
    /// as a helper inside a target session.
    /// Returns exit code: 0 = success, 1 = failure.
    /// </summary>
    public static int ExecuteSetAudio(string deviceId)
    {
        try
        {
            using var client = new AudioPolicyClient();
            client.SetDefaultEndpoint(deviceId);
            return 0;
        }
        catch
        {
            return 1;
        }
    }
}
