using System.Runtime.InteropServices;
using MultiSeat.Service.Interop;

namespace MultiSeat.Service.Audio;

/// <summary>
/// Represents a discovered audio endpoint device.
/// </summary>
public sealed class AudioEndpointInfo
{
    /// <summary>
    /// The Windows device ID string, e.g. "{0.0.0.00000000}.{some-guid}".
    /// This is what IPolicyConfig.SetDefaultEndpoint expects.
    /// </summary>
    public required string DeviceId { get; init; }

    /// <summary>
    /// Full friendly name, e.g. "Line 1 (Virtual Audio Cable)" or "Speakers (Realtek Audio)".
    /// </summary>
    public required string FriendlyName { get; init; }

    /// <summary>
    /// Shorter interface name, e.g. "Virtual Audio Cable" or "Realtek Audio".
    /// </summary>
    public string InterfaceName { get; init; } = string.Empty;

    /// <summary>
    /// Device state (Active, Disabled, etc.)
    /// </summary>
    public ComInterfaces.DeviceState State { get; init; }

    /// <summary>
    /// True if this endpoint appears to be a Virtual Audio Cable device.
    /// Detection heuristic: friendly name contains "Virtual Audio Cable", "VB-Audio",
    /// "VoiceMeeter", or "VAC".
    /// </summary>
    public bool IsVac { get; init; }

    /// <summary>
    /// Parsed cable index for VAC devices (1-based from "Line N").
    /// -1 if not parseable or not a VAC device.
    /// </summary>
    public int VacCableIndex { get; init; } = -1;

    public override string ToString() => $"{FriendlyName} [{DeviceId}] (VAC={IsVac}, Cable={VacCableIndex})";
}

/// <summary>
/// Enumerates audio endpoints on the system using the Windows Core Audio API
/// (IMMDeviceEnumerator → IMMDeviceCollection → IMMDevice → IPropertyStore).
///
/// This is the official, documented, stable way to discover audio devices.
/// Works on all Windows 10/11 builds.
///
/// Discovers all render (output) endpoints and identifies Virtual Audio Cable
/// devices by name pattern matching.
/// </summary>
public sealed class AudioDeviceEnumerator : IDisposable
{
    private readonly ILogger<AudioDeviceEnumerator> _logger;
    private ComInterfaces.IMMDeviceEnumerator? _enumerator;

    // Name fragments that identify known virtual audio cable software
    private static readonly string[] VacNamePatterns =
    [
        "Virtual Audio Cable",
        "VB-Audio",
        "VoiceMeeter",
        "CABLE Output",
        "Hi-Fi Cable"
    ];

    public AudioDeviceEnumerator(ILogger<AudioDeviceEnumerator> logger)
    {
        _logger = logger;
        InitializeEnumerator();
    }

    /// <summary>
    /// Enumerate all active audio render (output) endpoints on the system.
    /// Returns every device, not just VAC cables.
    /// </summary>
    public List<AudioEndpointInfo> EnumerateRenderEndpoints(
        ComInterfaces.DeviceState stateMask = ComInterfaces.DeviceState.Active)
    {
        var results = new List<AudioEndpointInfo>();

        if (_enumerator is null)
        {
            _logger.LogWarning("IMMDeviceEnumerator not available — cannot enumerate audio devices");
            return results;
        }

        var hr = _enumerator.EnumAudioEndpoints(
            ComInterfaces.EDataFlow.eRender,
            stateMask,
            out var collection);

        if (hr != 0)
        {
            _logger.LogError("EnumAudioEndpoints failed: HRESULT 0x{Hr:X8}", hr);
            return results;
        }

        try
        {
            collection.GetCount(out var count);
            _logger.LogDebug("Found {Count} render endpoints", count);

            for (int i = 0; i < count; i++)
            {
                try
                {
                    var endpoint = ReadEndpoint(collection, i);
                    if (endpoint is not null)
                        results.Add(endpoint);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read endpoint at index {Index}", i);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }

        return results;
    }

    /// <summary>
    /// Find all Virtual Audio Cable endpoints.
    /// Convenience wrapper that filters EnumerateRenderEndpoints() to VAC devices only.
    /// </summary>
    public List<AudioEndpointInfo> FindVacEndpoints()
    {
        var all = EnumerateRenderEndpoints();
        var vac = all.Where(e => e.IsVac).OrderBy(e => e.VacCableIndex).ToList();

        _logger.LogInformation("Discovered {Count} VAC endpoints out of {Total} total render devices",
            vac.Count, all.Count);

        foreach (var ep in vac)
        {
            _logger.LogDebug("  VAC: {Name} (cable {Index}) → {Id}",
                ep.FriendlyName, ep.VacCableIndex, ep.DeviceId);
        }

        return vac;
    }

    /// <summary>
    /// Look up a specific device by its device ID string.
    /// Returns null if the device doesn't exist or can't be read.
    /// </summary>
    public AudioEndpointInfo? GetEndpointById(string deviceId)
    {
        if (_enumerator is null) return null;

        var hr = _enumerator.GetDevice(deviceId, out var device);
        if (hr != 0)
        {
            _logger.LogDebug("GetDevice failed for '{Id}': HRESULT 0x{Hr:X8}", deviceId, hr);
            return null;
        }

        try
        {
            return ReadDeviceProperties(device, deviceId);
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    /// <summary>
    /// Get the current default render endpoint for the Console role.
    /// </summary>
    public AudioEndpointInfo? GetDefaultRenderEndpoint()
    {
        if (_enumerator is null) return null;

        var hr = _enumerator.GetDefaultAudioEndpoint(
            ComInterfaces.EDataFlow.eRender,
            ComInterfaces.ERole.eConsole,
            out var device);

        if (hr != 0)
        {
            _logger.LogDebug("GetDefaultAudioEndpoint failed: HRESULT 0x{Hr:X8}", hr);
            return null;
        }

        try
        {
            device.GetId(out var deviceId);
            return ReadDeviceProperties(device, deviceId);
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    public void Dispose()
    {
        if (_enumerator is not null)
        {
            Marshal.ReleaseComObject(_enumerator);
            _enumerator = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PRIVATE
    // ═══════════════════════════════════════════════════════════════════

    private void InitializeEnumerator()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(ComInterfaces.CLSID_MMDeviceEnumerator, throwOnError: true)!;
            var obj = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Failed to create MMDeviceEnumerator");

            _enumerator = (ComInterfaces.IMMDeviceEnumerator)obj;
            _logger.LogDebug("IMMDeviceEnumerator initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize IMMDeviceEnumerator COM object");
            _enumerator = null;
        }
    }

    private AudioEndpointInfo? ReadEndpoint(ComInterfaces.IMMDeviceCollection collection, int index)
    {
        var hr = collection.Item(index, out var device);
        if (hr != 0)
            return null;

        try
        {
            device.GetId(out var deviceId);
            return ReadDeviceProperties(device, deviceId);
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    private AudioEndpointInfo? ReadDeviceProperties(ComInterfaces.IMMDevice device, string deviceId)
    {
        device.GetState(out var state);

        var hr = device.OpenPropertyStore(ComInterfaces.STGM_READ, out var propStore);
        if (hr != 0)
        {
            _logger.LogDebug("OpenPropertyStore failed for {Id}: 0x{Hr:X8}", deviceId, hr);
            return new AudioEndpointInfo
            {
                DeviceId = deviceId,
                FriendlyName = deviceId, // fallback
                State = state,
                IsVac = false
            };
        }

        try
        {
            var friendlyName = ReadStringProperty(propStore, ComInterfaces.PKEY_Device_FriendlyName) ?? deviceId;
            var interfaceName = ReadStringProperty(propStore, ComInterfaces.PKEY_DeviceInterface_FriendlyName) ?? "";

            var isVac = IsVacDevice(friendlyName, interfaceName);
            var cableIndex = isVac ? ParseVacCableIndex(friendlyName) : -1;

            return new AudioEndpointInfo
            {
                DeviceId = deviceId,
                FriendlyName = friendlyName,
                InterfaceName = interfaceName,
                State = state,
                IsVac = isVac,
                VacCableIndex = cableIndex
            };
        }
        finally
        {
            Marshal.ReleaseComObject(propStore);
        }
    }

    private static string? ReadStringProperty(ComInterfaces.IPropertyStore propStore, ComInterfaces.PropertyKey key)
    {
        var hr = propStore.GetValue(ref key, out var pv);
        if (hr != 0)
            return null;

        var result = pv.GetString();

        // Clean up the PROPVARIANT's string allocation
        if (pv.vt == ComInterfaces.PropVariant.VT_LPWSTR && pv.data1 != IntPtr.Zero)
            Marshal.FreeCoTaskMem(pv.data1);

        return result;
    }

    /// <summary>
    /// Detect whether a device is a Virtual Audio Cable by name pattern matching.
    /// </summary>
    private static bool IsVacDevice(string friendlyName, string interfaceName)
    {
        foreach (var pattern in VacNamePatterns)
        {
            if (friendlyName.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                interfaceName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Parse the cable index from a VAC device name.
    /// Index assignment (stable ordering for seat assignment):
    ///   1 → VB-CABLE basic  "CABLE Output (VB-Audio Virtual Cable)"
    ///   2 → VoiceMeeter     "VoiceMeeter Input"
    ///   3 → VoiceMeeter Aux "VoiceMeeter Aux Input"
    ///   4 → VoiceMeeter V3  "VoiceMeeter VAIO3 Input"
    ///   N → Line N (Virtual Audio Cable)
    /// </summary>
    private static int ParseVacCableIndex(string friendlyName)
    {
        // VoiceMeeter Potato virtual devices (indices 2-4, leaving 1 for VB-CABLE basic)
        if (friendlyName.Contains("VoiceMeeter", StringComparison.OrdinalIgnoreCase))
        {
            if (friendlyName.Contains("VAIO3", StringComparison.OrdinalIgnoreCase))
                return 4;
            if (friendlyName.Contains("Aux", StringComparison.OrdinalIgnoreCase))
                return 3;
            return 2;
        }

        // VB-CABLE basic: "CABLE Output (VB-Audio Virtual Cable)" → 1
        if (friendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
            return 1;

        // Legacy: "Line N (Virtual Audio Cable)" → N
        if (friendlyName.StartsWith("Line ", StringComparison.OrdinalIgnoreCase))
        {
            var spaceIdx = friendlyName.IndexOf(' ', 5);
            if (spaceIdx < 0) spaceIdx = friendlyName.Length;
            var numStr = friendlyName.AsSpan(5, spaceIdx - 5);
            if (int.TryParse(numStr, out var n))
                return n;
        }

        return 1;
    }
}
