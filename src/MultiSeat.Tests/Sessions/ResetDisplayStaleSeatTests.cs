using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MultiSeat.Service.Accounts;
using MultiSeat.Service.Audio;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Display;
using MultiSeat.Service.Emulators;
using MultiSeat.Service.Input;
using MultiSeat.Service.Monitoring;
using MultiSeat.Service.Sessions;
using MultiSeat.Service.Storage;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// Regression tests for the display-reset stale-seat race: <see cref="SeatManager.ResetDisplayAsync"/>
/// was ungated, so a teardown that acquires <see cref="SeatLifecycleGate"/> and removes the seat could
/// interleave between <c>DestroyDisplayAsync</c> and <c>CreateDisplayAsync</c> + <c>UpdateDisplayOutput</c>,
/// re-registering the display assignment and rewriting the seat's Apollo config after teardown released
/// them — an orphan display record + resurrected config with no seat in <c>_seats</c> to ever own them
/// (same race class as ResetController / SetResolutionAsync / LaunchAppInSeatAsync, which are gated).
///
/// The fix wraps the whole destroy → create → config-write transaction in the per-seat lifecycle gate
/// and re-validates seat lifecycle state (status != TearingDown) AFTER the gate is acquired. A
/// <c>ResetDisplayAsync</c> that captured a seat before teardown and only then enters the gate must see
/// the post-teardown state and abort before any side effect.
/// </summary>
public class ResetDisplayStaleSeatTests
{
    private static readonly Guid SeatA = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // ── Pure predicate (mirrors ControllerResetStillValid / ResolutionChangeStillValid) ──

    [Theory]
    [InlineData(SeatStatus.Idle)]
    [InlineData(SeatStatus.Provisioning)]
    [InlineData(SeatStatus.Configuring)]
    [InlineData(SeatStatus.Ready)]
    [InlineData(SeatStatus.Streaming)]
    [InlineData(SeatStatus.Connecting)]
    [InlineData(SeatStatus.Error)]
    public void DisplayResetStillValid_AllowsValidStates(SeatStatus status)
    {
        // Display reset is a repair action offered on any non-tearing-down seat — a misbehaving
        // display can be reset from Ready, Streaming, or even Error. The only invalidation is
        // TearingDown, because by then the seat is no longer a member of _seats and re-running
        // destroy/create would orphan the display record.
        Assert.True(SeatManager.DisplayResetStillValid(status),
            $"Status {status} should be a valid precondition for ResetDisplayAsync");
    }

    [Fact]
    public void DisplayResetStillValid_RejectsTearingDown()
    {
        // TearingDown is the H2/H3 "removed" signal: a concurrent teardown removed the seat from
        // _seats while the request waited for the gate, so the captured object now reads
        // TearingDown. Re-running destroy + create for such a seat would re-register the display
        // assignment nothing in _seats would ever release again.
        Assert.False(SeatManager.DisplayResetStillValid(SeatStatus.TearingDown));
    }

    // ── Race scenario tests ─────────────────────────────────────────────

    [Fact]
    public async Task ResetDisplay_AfterTeardown_ThrowsSeatNotFound()
    {
        // Teardown first, then reset: the seat is no longer registered, so the pre-gate GetSeat
        // rejects the call before any gate or display work (the API endpoint surfaces this as 404
        // via its own GetSeat pre-check).
        var (mgr, display) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA);
            await mgr.TeardownSeatAsync(SeatA, CancellationToken.None);
            Assert.Null(mgr.GetSeat(SeatA));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => mgr.ResetDisplayAsync(SeatA, CancellationToken.None));
            Assert.Equal("Seat not found.", ex.Message);
            Assert.Equal(0, display.CreateCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task ResetDisplay_RemovedWhileWaitingForGate_DoesNotReset()
    {
        // The core race (H2 ordering): the seat is captured while registered, then a teardown
        // removes it from _seats while the reset waits for the gate. After the gate the post-gate
        // GetSeat re-check must return null and abort before any side effect. Deterministic: we
        // hold the gate so the reset is guaranteed to have resolved the seat and be blocked on
        // AcquireAsync before we remove it — no timing dependence.
        var (mgr, display) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA);

            // Hold the gate as a teardown would while removing the seat from the registry.
            using var lease = await mgr.LifecycleGate.AcquireAsync(SeatA, CancellationToken.None);

            // Start the reset: runs synchronously through GetSeat, then blocks on the gate we hold.
            var resetTask = mgr.ResetDisplayAsync(SeatA, CancellationToken.None);

            // Simulate teardown's TryRemove: remove the seat from _seats under the held gate.
            RemoveSeat(mgr, SeatA);

            // Release the gate; the reset's post-gate GetSeat must now return null and abort.
            lease.Dispose();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => resetTask);
            Assert.Equal("Seat was removed while resetting display.", ex.Message);
            Assert.Equal(0, display.CreateCount);
            Assert.Equal(0, display.DestroyCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task ResetDisplay_SeatFlippedToTearingDown_WhileWaitingForGate_DoesNotReset()
    {
        // Defensive variant of the race: the captured seat object reads TearingDown after the gate
        // (a teardown that flipped the status on the still-referenced object). The status
        // predicate, not just the null re-check, must reject it.
        var (mgr, display) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA);

            using var lease = await mgr.LifecycleGate.AcquireAsync(SeatA, CancellationToken.None);
            var resetTask = mgr.ResetDisplayAsync(SeatA, CancellationToken.None);

            // Flip the status on the registered seat to TearingDown (the H2 "removed" signal).
            mgr.GetSeat(SeatA)!.TransitionTo(SeatStatus.TearingDown, NullLogger.Instance);

            lease.Dispose();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => resetTask);
            Assert.Equal("Seat was removed while resetting display.", ex.Message);
            Assert.Equal(0, display.CreateCount);
            Assert.Equal(0, display.DestroyCount);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task ResetDisplay_GateSerialization_TeardownCannotRemoveSeatMidReset()
    {
        // Deterministic serialization proof (no sleeps): the reset is parked INSIDE its first
        // display side effect (DestroyDisplayAsync blocks on a test signal), which is only
        // possible while the reset holds the per-seat gate. A teardown started at that moment
        // therefore cannot remove the seat — removal needs the same gate — so the reset completes
        // its whole destroy → create → config transaction against a still-registered seat, and
        // only then does teardown run. If the gate were not held across the transaction (the
        // pre-fix behavior), teardown could remove the seat mid-reset and the create would orphan
        // the display record.
        var (mgr, display) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA);

            // Park the reset's first display call so we know, deterministically, that the reset
            // is mid-transaction and holding the gate when we start the teardown.
            display.ParkDestroy = true;
            var resetTask = mgr.ResetDisplayAsync(SeatA, CancellationToken.None);
            await display.DestroyStarted.Task; // reset is now inside Destroy, holding the gate

            // Teardown started now cannot remove the seat: removal needs the same gate, which the
            // reset holds. The seat must still be registered at this instant.
            Assert.NotNull(mgr.GetSeat(SeatA));
            var teardownTask = Task.Run(() => mgr.TeardownSeatAsync(SeatA, CancellationToken.None));

            // Unpark: the reset finishes destroy + create + config while still holding the gate,
            // so the whole transaction ran against a still-registered seat.
            display.ParkDestroy = false;
            display.DestroyGate.TrySetResult();

            await resetTask;
            Assert.Equal(1, display.DestroyCount);
            Assert.Equal(1, display.CreateCount);

            // Teardown runs only after the reset released the gate, then removes the seat.
            await teardownTask;
            Assert.Null(mgr.GetSeat(SeatA));
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task ResetDisplay_OnLiveReadySeat_ResetsDisplay()
    {
        // Valid path: reset on a Ready seat must still work end-to-end after the fix — the gate is
        // acquired and released, and the display is destroyed + recreated.
        var (mgr, display) = BuildSeatManager();
        try
        {
            RegisterReadySeat(mgr, SeatA);
            await mgr.ResetDisplayAsync(SeatA, CancellationToken.None);

            Assert.Equal(1, display.DestroyCount);
            Assert.Equal(1, display.CreateCount);
            Assert.NotNull(mgr.GetSeat(SeatA));
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static (SeatManager mgr, RecordingDisplayManager display) BuildSeatManager()
    {
        var options = new MultiSeatOptions
        {
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-display-{Guid.NewGuid():N}"),
            ApolloExePath = @"C:\never\apollo.exe"
        };

        var configBuilder = new ApolloConfigBuilder(
            new TestLogger<ApolloConfigBuilder>(),
            Options.Create(options));
        var serverQuery = new ApolloServerQuery(new TestLogger<ApolloServerQuery>());
        var sessionLauncher = new SessionLauncher(
            new TestLogger<SessionLauncher>(),
            Options.Create(options),
            Mock.Of<IAccountManager>());
        var processInjector = new ProcessInjector(
            new TestLogger<ProcessInjector>(),
            Options.Create(options),
            sessionLauncher);
        var display = new RecordingDisplayManager();
        var apolloManager = new ApolloManager(
            new TestLogger<ApolloManager>(),
            Options.Create(options),
            configBuilder,
            processInjector,
            serverQuery,
            Mock.Of<IProcessTracker>(),
            Mock.Of<IProcessMonitor>());

        var portAllocator = new PortAllocator();
        var firewall = new FirewallManager(new TestLogger<FirewallManager>(), Options.Create(options));
        var audioDeviceEnumerator = new AudioDeviceEnumerator(new TestLogger<AudioDeviceEnumerator>());
        var audioRouter = new AudioRouter(
            new TestLogger<AudioRouter>(),
            Options.Create(options),
            audioDeviceEnumerator,
            processInjector);
        var controllerManager = new ControllerManager(new TestLogger<ControllerManager>());
        var inputRouter = new InputRouter(new TestLogger<InputRouter>(), controllerManager);
        var inputHookManager = new InputHookManager(
            new TestLogger<InputHookManager>(), Options.Create(options));
        var hidHide = new HidHideConfigurator(new TestLogger<HidHideConfigurator>(), Options.Create(options));
        var onConnect = new OnConnectAppLauncher(
            new TestLogger<OnConnectAppLauncher>(),
            Options.Create(options),
            apolloManager,
            processInjector);
        var gate = new SeatLifecycleGate();

        var mgr = new SeatManager(
            new NullLogger<SeatManager>(),
            Options.Create(options),
            Mock.Of<IAccountManager>(),
            sessionLauncher,
            processInjector,
            display, // recording fake — records destroy/create without touching real display state
            apolloManager, // also satisfies IStreamingProvider (ApolloManager : IStreamingProvider)
            apolloManager,
            portAllocator,
            firewall,
            audioRouter,
            controllerManager,
            inputRouter,
            inputHookManager,
            hidHide,
            onConnect,
            Array.Empty<IEmulatorConfigSeeder>(),
            gate);

        return (mgr, display);
    }

    private static void RegisterReadySeat(SeatManager mgr, Guid seatId)
    {
        var seat = new SeatInfo
        {
            Id = seatId,
            AccountName = $"Test-{seatId:N}",
            Status = SeatStatus.Ready,
            SessionId = 0
        };
        // Use the existing internal TryRegisterSeat seam.
        var seatsDict = (System.Collections.Concurrent.ConcurrentDictionary<Guid, SeatInfo>)typeof(SeatManager)
            .GetField("_seats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(mgr)!;
        var ownershipLock = typeof(SeatManager)
            .GetField("_accountOwnershipLock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(mgr)!;
        Assert.True(SeatManager.TryRegisterSeat(seatsDict, ownershipLock, seat),
            "Test setup: seat registration should succeed");
    }

    private static void RemoveSeat(SeatManager mgr, Guid seatId)
    {
        // Mirror teardown's TryRemove from the internal registry, without running the whole
        // teardown pipeline (display/Apollo/session cleanup needs real components).
        var seatsDict = (System.Collections.Concurrent.ConcurrentDictionary<Guid, SeatInfo>)typeof(SeatManager)
            .GetField("_seats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(mgr)!;
        Assert.True(seatsDict.TryRemove(seatId, out _),
            "Test setup: seat should be registered before removal");
    }

    /// <summary>
    /// Records every destroy/create call so tests can assert the post-gate validation prevents
    /// them. Bypasses the real display-assignment bookkeeping. Optionally parks the FIRST
    /// DestroyDisplayAsync on a signal so a test can hold a reset mid-transaction (and therefore
    /// holding the per-seat gate) without timing dependence.
    /// </summary>
    private sealed class RecordingDisplayManager : IVirtualDisplayManager
    {
        private int _destroyCount;
        private int _createCount;

        public int DestroyCount => _destroyCount;
        public int CreateCount => _createCount;
        public bool IsDriverAvailable => true;
        public IReadOnlyList<object> EnumerateAllConnectedPaths() => [];

        /// <summary>When true, the first DestroyDisplayAsync parks until DestroyGate fires.</summary>
        public volatile bool ParkDestroy;

        /// <summary>Signals that the parked DestroyDisplayAsync has been entered.</summary>
        public TaskCompletionSource DestroyStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Release a parked DestroyDisplayAsync.</summary>
        public TaskCompletionSource DestroyGate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task CreateDisplayAsync(SeatInfo seat, CancellationToken ct)
        {
            System.Threading.Interlocked.Increment(ref _createCount);
            await Task.CompletedTask;
        }

        public async Task DestroyDisplayAsync(SeatInfo seat, CancellationToken ct)
        {
            System.Threading.Interlocked.Increment(ref _destroyCount);
            if (ParkDestroy)
            {
                ParkDestroy = false;
                DestroyStarted.TrySetResult();
                await DestroyGate.Task;
            }
            else
            {
                await Task.CompletedTask;
            }
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        { }
    }
}
