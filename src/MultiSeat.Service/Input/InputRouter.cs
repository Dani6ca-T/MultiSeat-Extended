using System.Collections.Concurrent;
using MultiSeat.Service.Interop;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Input;

/// <summary>
/// Bridges physical XInput gamepads to ViGEm virtual controllers.
///
/// Architecture:
///   - A dedicated polling thread reads XInput state at ~1ms intervals
///     (matching USB HID polling rate for minimum input latency)
///   - Each physical XInput index (0-3) can be mapped to a seat
///   - Physical state is translated to Xbox360Report and submitted
///     to the seat's ViGEm virtual controller
///   - Vibration feedback flows backwards: game → ViGEm → XInput.SetState
///
/// For handheld devices (ROG Ally X, Steam Deck, etc.):
///   The built-in gamepad typically appears as XInput index 0.
///   When the handheld is the host, index 0 can be routed to any seat.
///
/// Controller assignment:
///   - Auto-assign: first connected controller → first seat without one
///   - Manual: API endpoint to assign XInput index → seat ID
///   - Handheld mode: single controller shared via Apollo's virtual input
///     (Moonlight sends input to Apollo, which creates a virtual gamepad
///     within the session — no physical passthrough needed)
/// </summary>
public sealed class InputRouter : IDisposable
{
    private readonly ILogger<InputRouter> _logger;
    private readonly ControllerManager _controllerManager;

    // XInput index (0-3) → assigned seat ID
    private readonly ConcurrentDictionary<int, Guid> _assignments = new();

    // Polling thread state
    private Thread? _pollThread;
    private volatile bool _running;

    // Track last state to avoid redundant ViGEm submissions
    private readonly XInput.State[] _lastStates = new XInput.State[XInput.XUSER_MAX_COUNT];
    private readonly bool[] _connected = new bool[XInput.XUSER_MAX_COUNT];

    public InputRouter(ILogger<InputRouter> logger, ControllerManager controllerManager)
    {
        _logger = logger;
        _controllerManager = controllerManager;

        // Subscribe to vibration feedback from virtual controllers
        _controllerManager.VibrationReceived += OnVibrationFeedback;
    }

    /// <summary>
    /// Start the XInput polling loop on a dedicated background thread.
    /// </summary>
    public void Start()
    {
        if (_running) return;

        _running = true;
        _pollThread = new Thread(PollLoop)
        {
            Name = "MultiSeat.InputRouter.XInput",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal // input latency matters
        };
        _pollThread.Start();

        _logger.LogInformation("Input router started — polling XInput at ~1ms");
    }

    /// <summary>
    /// Stop the polling loop.
    /// </summary>
    public void Stop()
    {
        _running = false;
        _pollThread?.Join(2000);
        _pollThread = null;
        _logger.LogInformation("Input router stopped");
    }

    /// <summary>
    /// Assign a physical XInput controller to a seat.
    /// </summary>
    public void AssignController(int xinputIndex, Guid seatId)
    {
        if (xinputIndex is < 0 or >= XInput.XUSER_MAX_COUNT)
            throw new ArgumentOutOfRangeException(nameof(xinputIndex),
                $"XInput index must be 0-{XInput.XUSER_MAX_COUNT - 1}");

        _assignments[xinputIndex] = seatId;
        _logger.LogInformation("XInput {Idx} → Seat {Id}", xinputIndex, seatId);
    }

    /// <summary>
    /// Remove a controller-to-seat assignment.
    /// </summary>
    public void UnassignController(int xinputIndex)
    {
        if (_assignments.TryRemove(xinputIndex, out var seatId))
            _logger.LogInformation("XInput {Idx} unassigned from seat {Id}", xinputIndex, seatId);
    }

    /// <summary>
    /// Auto-assign all connected XInput controllers to seats in order.
    /// </summary>
    public void AutoAssign(IReadOnlyList<SeatInfo> seats)
    {
        _assignments.Clear();

        var seatIndex = 0;
        for (int i = 0; i < XInput.XUSER_MAX_COUNT && seatIndex < seats.Count; i++)
        {
            if (XInput.GetState(i, out _) == XInput.ERROR_SUCCESS)
            {
                var seat = seats[seatIndex++];
                _assignments[i] = seat.Id;
                _logger.LogInformation("Auto-assigned XInput {Idx} → Seat {Id} ({Account})",
                    i, seat.Id, seat.AccountName);
            }
        }
    }

    /// <summary>
    /// Get the current assignments.
    /// </summary>
    public IReadOnlyDictionary<int, Guid> GetAssignments() =>
        _assignments.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    /// <summary>
    /// Get connected XInput controller indices.
    /// </summary>
    public List<int> GetConnectedControllers()
    {
        var result = new List<int>();
        for (int i = 0; i < XInput.XUSER_MAX_COUNT; i++)
        {
            if (XInput.GetState(i, out _) == XInput.ERROR_SUCCESS)
                result.Add(i);
        }
        return result;
    }

    public void Dispose()
    {
        Stop();
        _controllerManager.VibrationReceived -= OnVibrationFeedback;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PRIVATE — Polling loop
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// High-frequency XInput polling loop.
    /// Runs on a dedicated thread at ~1ms intervals.
    /// </summary>
    private void PollLoop()
    {
        _logger.LogDebug("XInput poll loop started on thread {ThreadId}",
            Environment.CurrentManagedThreadId);

        while (_running)
        {
            for (int i = 0; i < XInput.XUSER_MAX_COUNT; i++)
            {
                var result = XInput.GetState(i, out var state);

                if (result == XInput.ERROR_SUCCESS)
                {
                    if (!_connected[i])
                    {
                        _connected[i] = true;
                        _logger.LogInformation("XInput controller {Idx} connected", i);
                    }

                    // Only forward if state changed (dwPacketNumber increments on change)
                    if (state.dwPacketNumber != _lastStates[i].dwPacketNumber)
                    {
                        _lastStates[i] = state;
                        ForwardToSeat(i, ref state);
                    }
                }
                else if (_connected[i])
                {
                    _connected[i] = false;
                    _logger.LogInformation("XInput controller {Idx} disconnected", i);
                }
            }

            // Sleep 1ms — matches USB HID polling rate
            // Thread.Sleep(1) actually sleeps ~1-2ms on Windows with default timer resolution.
            // For sub-millisecond polling, we'd use a multimedia timer or busy-wait,
            // but 1ms is sufficient for game streaming (Moonlight polls at ~4ms anyway).
            Thread.Sleep(1);
        }
    }

    /// <summary>
    /// Forward XInput state to the ViGEm virtual controller assigned to this index's seat.
    /// </summary>
    private void ForwardToSeat(int xinputIndex, ref XInput.State state)
    {
        if (!_assignments.TryGetValue(xinputIndex, out var seatId))
            return; // not assigned to any seat

        _controllerManager.SubmitState(seatId, ref state.Gamepad);
    }

    /// <summary>
    /// Forward vibration feedback from the virtual controller back to
    /// the physical XInput controller.
    /// </summary>
    private void OnVibrationFeedback(object? sender, VibrationEventArgs e)
    {
        // Find which XInput index is assigned to this seat
        foreach (var (xinputIdx, seatId) in _assignments)
        {
            if (seatId == e.SeatId)
            {
                var vibration = new XInput.Vibration
                {
                    // XInput vibration uses 0-65535 range, ViGEm uses 0-255.
                    // Scale up.
                    wLeftMotorSpeed = (ushort)(e.LargeMotor * 257),
                    wRightMotorSpeed = (ushort)(e.SmallMotor * 257)
                };

                XInput.SetState(xinputIdx, ref vibration);
                return;
            }
        }
    }
}
