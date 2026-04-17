using System.Collections.Concurrent;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using MultiSeat.Service.Interop;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Input;

/// <summary>
/// Manages ViGEmBus virtual Xbox 360 controllers for each seat.
///
/// Lifecycle per seat:
///   1. CreateController() → allocates and connects a new virtual X360 gamepad
///   2. InputRouter feeds physical gamepad state → SubmitReport() on the virtual pad
///   3. DestroyController() → disconnects and disposes on seat teardown
///
/// Each virtual controller appears as a real Xbox 360 gamepad to the
/// seat's session. Apollo picks it up natively for Moonlight streaming.
///
/// Vibration feedback from the game flows back through ViGEm's
/// FeedbackReceived callback → forwarded to the physical controller via XInput.
///
/// Requires ViGEmBus driver v1.21+:
///   https://github.com/nefarius/ViGEmBus/releases
/// </summary>
public sealed class ControllerManager : IDisposable
{
    private readonly ILogger<ControllerManager> _logger;
    private ViGEmClient? _client;
    private bool _driverAvailable;

    // Seat → virtual controller mapping
    private readonly ConcurrentDictionary<Guid, VirtualController> _controllers = new();

    public ControllerManager(ILogger<ControllerManager> logger)
    {
        _logger = logger;
        InitializeClient();
    }

    /// <summary>
    /// True if the ViGEmBus driver is installed and the client connected successfully.
    /// </summary>
    public bool IsDriverAvailable => _driverAvailable;

    /// <summary>
    /// Get the number of active virtual controllers.
    /// </summary>
    public int ActiveControllerCount => _controllers.Count;

    /// <summary>
    /// Create and connect a virtual Xbox 360 controller for the seat.
    /// Returns the controller index (1-based).
    /// </summary>
    public int CreateController(SeatInfo seat)
    {
        if (!_driverAvailable || _client is null)
        {
            _logger.LogWarning(
                "ViGEmBus driver not available — seat {Id} will not have a virtual controller. " +
                "Install ViGEmBus from https://github.com/nefarius/ViGEmBus/releases",
                seat.Id);
            return -1;
        }

        try
        {
            var x360 = _client.CreateXbox360Controller();
            x360.AutoSubmitReport = false; // we submit manually for precise timing

            // Set up vibration feedback callback
            x360.FeedbackReceived += (_, e) =>
            {
                OnVibrationFeedback(seat.Id, e.LargeMotor, e.SmallMotor);
            };

            x360.Connect();

            var vc = new VirtualController(
                x360,
                ControllerIndex: _controllers.Count + 1,
                SeatId: seat.Id);

            _controllers.TryAdd(seat.Id, vc);

            _logger.LogInformation(
                "Virtual X360 controller #{Idx} created for seat {Id} " +
                "(ViGEm user index: {UserIdx})",
                vc.ControllerIndex, seat.Id, x360.UserIndex);

            return vc.ControllerIndex;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create virtual controller for seat {Id}", seat.Id);
            return -1;
        }
    }

    /// <summary>
    /// Translate physical XInput gamepad state and submit to a seat's virtual controller.
    /// Called by InputRouter on each poll cycle.
    /// </summary>
    public void SubmitState(Guid seatId, ref XInput.Gamepad gp)
    {
        if (!_controllers.TryGetValue(seatId, out var vc))
            return;

        try
        {
            var c = vc.Controller;

            // Buttons — pack XInput flags directly via SetButtonsFull
            c.SetButtonsFull((ushort)gp.wButtons);

            // Axes — direct passthrough (same range: -32768 to 32767)
            c.SetAxisValue(Xbox360Axis.LeftThumbX, gp.sThumbLX);
            c.SetAxisValue(Xbox360Axis.LeftThumbY, gp.sThumbLY);
            c.SetAxisValue(Xbox360Axis.RightThumbX, gp.sThumbRX);
            c.SetAxisValue(Xbox360Axis.RightThumbY, gp.sThumbRY);

            // Triggers — passthrough (0-255)
            c.SetSliderValue(Xbox360Slider.LeftTrigger, gp.bLeftTrigger);
            c.SetSliderValue(Xbox360Slider.RightTrigger, gp.bRightTrigger);

            c.SubmitReport();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to submit state to controller for seat {Id}", seatId);
        }
    }

    /// <summary>
    /// Get the device instance path of the seat's virtual controller.
    /// Used by HidHide for cloaking rules.
    /// </summary>
    public string? GetDeviceInstancePath(Guid seatId)
    {
        // ViGEm virtual controllers appear as:
        //   HID\VID_045E&PID_028E\...  (Xbox 360 controller VID/PID)
        // The exact instance path can be queried after Connect().
        // For now, we return a pattern-based path.

        if (!_controllers.TryGetValue(seatId, out var vc))
            return null;

        // The ViGEm client library doesn't expose the instance path directly.
        // We can determine it by querying SetupAPI for the most recently
        // connected device matching the ViGEm VID/PID.
        // For the MVP, return null and let HidHide operate on all ViGEm devices.
        _logger.LogDebug("Device instance path query for seat {Id} controller #{Idx}",
            seatId, vc.ControllerIndex);

        return null;
    }

    /// <summary>
    /// Disconnect and destroy the virtual controller for a seat.
    /// </summary>
    public void DestroyController(SeatInfo seat)
    {
        if (_controllers.TryRemove(seat.Id, out var vc))
        {
            try
            {
                vc.Controller.Disconnect();
                _logger.LogInformation(
                    "Virtual X360 controller #{Idx} destroyed for seat {Id}",
                    vc.ControllerIndex, seat.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Error disconnecting virtual controller #{Idx} for seat {Id}",
                    vc.ControllerIndex, seat.Id);
            }
        }
    }

    /// <summary>
    /// Disconnect and destroy all virtual controllers.
    /// Called on service shutdown.
    /// </summary>
    public void DestroyAll()
    {
        foreach (var (seatId, vc) in _controllers)
        {
            try
            {
                vc.Controller.Disconnect();
            }
            catch { /* best effort */ }
        }
        _controllers.Clear();
    }

    public void Dispose()
    {
        DestroyAll();
        _client?.Dispose();
        _client = null;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PRIVATE
    // ═══════════════════════════════════════════════════════════════════

    private void InitializeClient()
    {
        try
        {
            _client = new ViGEmClient();
            _driverAvailable = true;
            _logger.LogInformation("ViGEmBus client initialized successfully");
        }
        catch (Nefarius.ViGEm.Client.Exceptions.VigemBusNotFoundException)
        {
            _logger.LogWarning(
                "ViGEmBus driver not found. Virtual controller support disabled. " +
                "Install from https://github.com/nefarius/ViGEmBus/releases");
            _driverAvailable = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize ViGEmBus client");
            _driverAvailable = false;
        }
    }

    /// <summary>
    /// Called when the game sends vibration/rumble feedback through
    /// the virtual controller. We forward this to the physical controller
    /// via InputRouter.
    /// </summary>
    private void OnVibrationFeedback(Guid seatId, byte largeMotor, byte smallMotor)
    {
        VibrationReceived?.Invoke(this, new VibrationEventArgs(seatId, largeMotor, smallMotor));
    }

    /// <summary>
    /// Event raised when a game sends vibration feedback through a virtual controller.
    /// InputRouter subscribes to this to forward vibration to the physical pad.
    /// </summary>
    public event EventHandler<VibrationEventArgs>? VibrationReceived;
}

/// <summary>
/// Tracks a virtual controller and its metadata.
/// </summary>
internal sealed record VirtualController(
    IXbox360Controller Controller,
    int ControllerIndex,
    Guid SeatId);

/// <summary>
/// Vibration feedback event from a game through the virtual controller.
/// </summary>
public sealed class VibrationEventArgs : EventArgs
{
    public Guid SeatId { get; }
    public byte LargeMotor { get; }
    public byte SmallMotor { get; }

    public VibrationEventArgs(Guid seatId, byte largeMotor, byte smallMotor)
    {
        SeatId = seatId;
        LargeMotor = largeMotor;
        SmallMotor = smallMotor;
    }
}
