using System.Management;

namespace MultiSeat.Service.Input;

/// <summary>
/// Watches for USB HID device connect/disconnect events via WMI.
/// Fires events when gamepads are plugged in or removed, allowing
/// InputRouter to auto-assign or unassign controllers.
///
/// Uses WMI __InstanceCreationEvent / __InstanceDeletionEvent on
/// Win32_PnPEntity filtered to USB HID gaming devices.
/// </summary>
public sealed class DeviceWatcher : IDisposable
{
    private readonly ILogger<DeviceWatcher> _logger;
    private readonly InputRouter _inputRouter;
    private ManagementEventWatcher? _connectWatcher;
    private ManagementEventWatcher? _disconnectWatcher;

    public DeviceWatcher(ILogger<DeviceWatcher> logger, InputRouter inputRouter)
    {
        _logger = logger;
        _inputRouter = inputRouter;
    }

    /// <summary>
    /// Start watching for USB HID device events.
    /// </summary>
    public void Start()
    {
        try
        {
            // Watch for new USB device connections
            var connectQuery = new WqlEventQuery(
                "__InstanceCreationEvent",
                TimeSpan.FromSeconds(2),
                "TargetInstance ISA 'Win32_PnPEntity' AND " +
                "TargetInstance.PNPClass = 'HIDClass'");

            _connectWatcher = new ManagementEventWatcher(connectQuery);
            _connectWatcher.EventArrived += OnDeviceConnected;
            _connectWatcher.Start();

            // Watch for USB device disconnections
            var disconnectQuery = new WqlEventQuery(
                "__InstanceDeletionEvent",
                TimeSpan.FromSeconds(2),
                "TargetInstance ISA 'Win32_PnPEntity' AND " +
                "TargetInstance.PNPClass = 'HIDClass'");

            _disconnectWatcher = new ManagementEventWatcher(disconnectQuery);
            _disconnectWatcher.EventArrived += OnDeviceDisconnected;
            _disconnectWatcher.Start();

            _logger.LogInformation("HID device watcher started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start HID device watcher — " +
                "auto-assignment on plug/unplug will not work");
        }
    }

    /// <summary>
    /// Stop watching for device events.
    /// </summary>
    public void Stop()
    {
        _connectWatcher?.Stop();
        _disconnectWatcher?.Stop();
        _logger.LogInformation("HID device watcher stopped");
    }

    public void Dispose()
    {
        Stop();
        _connectWatcher?.Dispose();
        _disconnectWatcher?.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════

    private void OnDeviceConnected(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var target = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var deviceId = target["DeviceID"]?.ToString() ?? "unknown";
            var name = target["Name"]?.ToString() ?? "unknown";

            _logger.LogInformation("HID device connected: {Name} ({DeviceId})", name, deviceId);

            // Check if this is a gamepad by looking for gaming-related keywords
            if (IsGamepad(name, deviceId))
            {
                _logger.LogInformation("Gamepad detected: {Name}", name);
                DeviceConnected?.Invoke(this, new DeviceEventArgs(deviceId, name, true));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing device connect event");
        }
    }

    private void OnDeviceDisconnected(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var target = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var deviceId = target["DeviceID"]?.ToString() ?? "unknown";
            var name = target["Name"]?.ToString() ?? "unknown";

            _logger.LogInformation("HID device disconnected: {Name} ({DeviceId})", name, deviceId);

            if (IsGamepad(name, deviceId))
            {
                DeviceDisconnected?.Invoke(this, new DeviceEventArgs(deviceId, name, false));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing device disconnect event");
        }
    }

    private static bool IsGamepad(string name, string deviceId)
    {
        // Check for common gamepad VID/PIDs and name patterns
        var lower = (name + " " + deviceId).ToLowerInvariant();
        return lower.Contains("gamepad") ||
               lower.Contains("controller") ||
               lower.Contains("joystick") ||
               lower.Contains("xbox") ||
               lower.Contains("xinput") ||
               lower.Contains("vid_045e") || // Microsoft (Xbox controllers)
               lower.Contains("vid_054c") || // Sony (DualShock/DualSense)
               lower.Contains("vid_057e") || // Nintendo
               lower.Contains("vid_28de");   // Valve (Steam Controller)
    }

    /// <summary>Event raised when a gamepad is connected.</summary>
    public event EventHandler<DeviceEventArgs>? DeviceConnected;

    /// <summary>Event raised when a gamepad is disconnected.</summary>
    public event EventHandler<DeviceEventArgs>? DeviceDisconnected;
}

public sealed class DeviceEventArgs : EventArgs
{
    public string DeviceId { get; }
    public string DeviceName { get; }
    public bool Connected { get; }

    public DeviceEventArgs(string deviceId, string deviceName, bool connected)
    {
        DeviceId = deviceId;
        DeviceName = deviceName;
        Connected = connected;
    }
}
