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
/// Regression tests for the audio-reset stale-seat race: <see cref="SeatManager.ResetAudio"/> was
/// ungated, so a teardown that acquires <see cref="SeatLifecycleGate"/> could interleave between
/// <c>ReleaseCable</c> and <c>AssignCable</c> — releasing the seat's cable and then logging the session
/// off while the reset re-assigns. The re-assign lands on a seat that is being torn down: the
/// AudioRouter keeps a cable assignment whose seat no longer exists in <c>_seats</c>, that cable pair is
/// never released again (stays unavailable to every future seat), and the session-default helper runs
/// in a session that is being disconnected (same race class as ResetController / SetResolutionAsync /
/// LaunchAppInSeatAsync / ResetDisplayAsync, which are already gated).
///
/// The fix wraps the release → re-assign → re-apply transaction in the per-seat lifecycle gate and
/// re-validates seat lifecycle state (status != TearingDown) AFTER the gate is acquired. A
/// <c>ResetAudio</c> that captured a seat before teardown and only then enters the gate must see the
/// post-teardown state and abort before any audio side effect. Under <c>PerSession</c> audio the
/// operation remains a no-op (MultiSeat assigns no device; the session owns its Remote Audio
/// endpoint), and it stays a no-op before the gate so no gate is taken for a no-op call.
/// </summary>
public class ResetAudioStaleSeatTests
{
    private static readonly Guid SeatA = Guid.Parse("55555555-5555-5555-5555-555555555555");

    // ── Pure predicate (mirrors ControllerResetStillValid / DisplayResetStillValid) ──

    [Theory]
    [InlineData(SeatStatus.Idle)]
    [InlineData(SeatStatus.Provisioning)]
    [InlineData(SeatStatus.Configuring)]
    [InlineData(SeatStatus.Ready)]
    [InlineData(SeatStatus.Streaming)]
    [InlineData(SeatStatus.Connecting)]
    [InlineData(SeatStatus.Error)]
    public void AudioResetStillValid_AllowsValidStates(SeatStatus status)
    {
        // Audio reset is a repair action offered on any non-tearing-down seat — a misbehaving
        // cable can be reset from Ready, Streaming, or even Error. The only invalidation is
        // TearingDown, because by then the seat is no longer a member of _seats and re-assigning
        // would orphan the AudioRouter's cable assignment.
        Assert.True(SeatManager.AudioResetStillValid(status),
            $"Status {status} should be a valid precondition for ResetAudio");
    }

    [Fact]
    public void AudioResetStillValid_RejectsTearingDown()
    {
        // TearingDown is the H2/H3 "removed" signal: a concurrent teardown removed the seat from
        // _seats while the request waited for the gate, so the captured object now reads
        // TearingDown. Re-running AssignCable for such a seat would leave the AudioRouter holding
        // a cable assignment whose seat no longer exists in _seats — that cable is never released.
        Assert.False(SeatManager.AudioResetStillValid(SeatStatus.TearingDown));
    }

    // ── Behavior tests ─────────────────────────────────────────────────

    [Fact]
    public async Task ResetAudio_PerSession_IsNoOp()
    {
        // PerSession mode is the documented no-op (and must stay one): MultiSeat assigns no
        // device, and the session's Remote Audio endpoint lives and dies with the session itself.
        // The early return happens before the gate, so nothing is mutated and no gate is held.
        var (mgr, router, _) = BuildSeatManager(AudioMode.PerSession);
        try
        {
            RegisterSeat(mgr, SeatA, status: SeatStatus.Ready, sessionId: 7);
            await mgr.ResetAudio(SeatA);

            var seat = mgr.GetSeat(SeatA);
            Assert.NotNull(seat);
            Assert.Null(router.GetAssignment(SeatA));
            Assert.Equal(-1, seat!.VacCableIndex); // untouched by the no-op
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task ResetAudio_AfterTeardown_ThrowsSeatNotFound()
    {
        // Teardown first, then reset: the seat is no longer registered, so the pre-gate GetSeat
        // rejects the call before any gate or audio work (the API endpoint surfaces this as 404
        // via its own GetSeat pre-check).
        var (mgr, router, _) = BuildSeatManager(AudioMode.SharedHost);
        try
        {
            RegisterSeat(mgr, SeatA, status: SeatStatus.Ready, sessionId: 7);
            await mgr.TeardownSeatAsync(SeatA, CancellationToken.None);
            Assert.Null(mgr.GetSeat(SeatA));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => mgr.ResetAudio(SeatA));
            Assert.Equal("Seat not found.", ex.Message);
            Assert.Null(router.GetAssignment(SeatA));
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task ResetAudio_RemovedWhileWaitingForGate_DoesNotReset()
    {
        // The core race (H2 ordering): the seat is captured while registered, then a teardown
        // removes it from _seats while the reset waits for the gate. After the gate the post-gate
        // GetSeat re-check must return null and abort before ReleaseCable/AssignCable.
        // Deterministic: we hold the gate so the reset is guaranteed to have resolved the seat
        // and be blocked on AcquireAsync before we remove it — no timing dependence.
        var (mgr, router, _) = BuildSeatManager(AudioMode.SharedHost);
        try
        {
            RegisterSeat(mgr, SeatA, status: SeatStatus.Ready, sessionId: 7);

            // Hold the gate as a teardown would while removing the seat from the registry.
            using var lease = await mgr.LifecycleGate.AcquireAsync(SeatA, CancellationToken.None);

            // Start the reset: runs synchronously through GetSeat + PerSession check, then
            // blocks on the gate we hold.
            var resetTask = mgr.ResetAudio(SeatA);

            // Simulate teardown's TryRemove: remove the seat from _seats under the held gate.
            RemoveSeat(mgr, SeatA);

            // Release the gate; the reset's post-gate GetSeat must now return null and abort.
            lease.Dispose();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => resetTask);
            Assert.Equal("Seat was removed while resetting audio.", ex.Message);
            Assert.Null(router.GetAssignment(SeatA));
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task ResetAudio_SeatFlippedToTearingDown_WhileWaitingForGate_DoesNotReset()
    {
        // Defensive variant of the race: the captured seat object reads TearingDown after the
        // gate (a teardown that flipped the status on the still-referenced object). The status
        // predicate, not just the null re-check, must reject it.
        var (mgr, router, _) = BuildSeatManager(AudioMode.SharedHost);
        try
        {
            RegisterSeat(mgr, SeatA, status: SeatStatus.Ready, sessionId: 7);

            using var lease = await mgr.LifecycleGate.AcquireAsync(SeatA, CancellationToken.None);
            var resetTask = mgr.ResetAudio(SeatA);

            // Flip the status on the registered seat to TearingDown (the H2 "removed" signal).
            mgr.GetSeat(SeatA)!.TransitionTo(SeatStatus.TearingDown, NullLogger.Instance);

            lease.Dispose();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => resetTask);
            Assert.Equal("Seat was removed while resetting audio.", ex.Message);
            Assert.Null(router.GetAssignment(SeatA));
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    [Fact]
    public async Task ResetAudio_OnLiveReadySeat_ResetsAudio()
    {
        // Valid path: reset on a live SharedHost seat must still work end-to-end after the fix —
        // the gate is acquired and released, the cable is re-assigned, and the seat keeps its
        // registration. The AudioRouter is seeded with one free cable pair (via reflection over
        // its private scan state) so AssignCable is deterministic and touches no real audio
        // devices. No process is launched: the seeded pair has no mic-capture endpoint, so
        // ApplyAudioDefaults' helper invocation is skipped.
        var (mgr, router, seededIndex) = BuildSeatManager(AudioMode.SharedHost);
        try
        {
            RegisterSeat(mgr, SeatA, status: SeatStatus.Ready, sessionId: 7);
            await mgr.ResetAudio(SeatA);

            var seat = mgr.GetSeat(SeatA);
            Assert.NotNull(seat);
            Assert.NotNull(router.GetAssignment(SeatA));
            Assert.Equal(seededIndex, seat!.VacCableIndex);
            Assert.Equal(SeatStatus.Ready, seat.Status);
        }
        finally
        {
            mgr.ControllerManager.Dispose();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static (SeatManager mgr, AudioRouter router, int seededIndex) BuildSeatManager(
        AudioMode audioMode)
    {
        var options = new MultiSeatOptions
        {
            ApolloConfigDir = Path.Combine(Path.GetTempPath(), $"multiseat-audio-{Guid.NewGuid():N}"),
            ApolloExePath = @"C:\never\apollo.exe",
            AudioMode = audioMode
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
        var displayManager = new VirtualDisplayManager(
            new TestLogger<VirtualDisplayManager>(),
            Options.Create(options),
            processInjector);

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
            displayManager,
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

        // Seed one free cable so AssignCable under SharedHost is deterministic and touches no
        // real audio devices. MicCapture is null so the seat's AudioCaptureDeviceId stays empty
        // and ApplyAudioDefaults skips its helper-process invocation.
        var seededIndex = SeedFreeCable(audioRouter, audioMode);

        return (mgr, audioRouter, seededIndex);
    }

    /// <summary>
    /// Register <paramref name="seat"/> as a member of <c>_seats</c> with the given lifecycle
    /// status and session id, bypassing the full provisioning pipeline (accounts/session/display
    /// creation needs real infrastructure).
    /// </summary>
    private static void RegisterSeat(SeatManager mgr, Guid seatId, SeatStatus status, int sessionId)
    {
        var seat = new SeatInfo
        {
            Id = seatId,
            AccountName = $"Test-{seatId:N}",
            Status = status,
            SessionId = sessionId
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
    /// Make <see cref="AudioRouter.AssignCable"/> deterministic without audio hardware: add one
    /// free cable pair to the router's private pair list and mark its device scan as already
    /// done, so AssignCable never runs the COM/VoiceMeeter scan path. Returns the seeded cable
    /// index for assertions. No-op under PerSession (the router never assigns there).
    /// </summary>
    private static int SeedFreeCable(AudioRouter router, AudioMode audioMode)
    {
        if (audioMode == AudioMode.PerSession)
            return -1;

        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        var pair = new AudioSeatPair
        {
            GameRender = new AudioEndpointInfo
            {
                DeviceId = "{0.0.0.00000000}.{test-game}",
                FriendlyName = "Test Cable Input",
                IsVac = true,
                VacCableIndex = 4
            },
            MicCapture = null
        };

        var pairsField = typeof(AudioRouter).GetField("_vacPairs", flags)!;
        var pairs = (List<AudioSeatPair>)pairsField.GetValue(router)!;
        pairs.Add(pair);

        var scannedField = typeof(AudioRouter).GetField("_vacScanned", flags)!;
        scannedField.SetValue(router, true);

        return pair.GameRender.VacCableIndex;
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
