using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MultiSeat.Service.Sessions;
using MultiSeat.Shared.Models;
using Xunit;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// The transition table is only worth having if something checks it. It was decorative until the
/// 4544429 port — <c>CanTransitionTo</c> was called from tests that asserted the table against
/// itself, so transitions the production code performed every day went unnoticed, including
/// <c>Error -> Ready</c> from PR #22's recovery endpoint and <c>Ready -> Error</c> from the
/// health check.
///
/// Production logs an illegal transition and applies it anyway, because stranding a real seat over
/// a bookkeeping disagreement is worse than the disagreement. These tests are the other half:
/// strict mode is on for the whole test assembly, so the same mistake is fatal before it ships.
/// </summary>
public class SeatTransitionEnforcementTests
{
    [ModuleInitializer]
    internal static void EnableStrictTransitions() => SeatState.StrictTransitions = true;

    private static SeatInfo Seat(SeatStatus status) =>
        new() { AccountName = "MultiSeatSeat01", Status = status };

    /// <summary>
    /// Every transition the service actually performs. Adding a <c>TransitionTo</c> call to the
    /// service without adding it here is fine; removing a row that the service still performs is
    /// how the table drifted out of truth last time.
    ///
    /// MSE-specific: recovery only enters Connecting from Ready/Streaming (the guard in
    /// SessionHealthCheck.CheckSeatAsync), so Configuring -> Connecting is legal but never
    /// performed and deliberately absent here. The same-state rows are real: LaunchApp re-asserts
    /// Streaming, and recovery restores mid-provision seats to themselves.
    /// </summary>
    public static TheoryData<SeatStatus, SeatStatus, string> RealTransitions() => new()
    {
        { SeatStatus.Provisioning, SeatStatus.Configuring,  "SeatManager: provisioning pipeline" },
        { SeatStatus.Provisioning, SeatStatus.Error,        "SeatManager: provisioning threw" },
        { SeatStatus.Provisioning, SeatStatus.TearingDown,  "SeatManager: teardown mid-provision" },
        { SeatStatus.Configuring,  SeatStatus.Ready,        "SeatManager: provisioning complete" },
        { SeatStatus.Configuring,  SeatStatus.Error,        "SeatManager: provisioning threw later; HealthCheck: session died mid-provision" },
        { SeatStatus.Configuring,  SeatStatus.TearingDown,  "SeatManager: teardown mid-configuring" },
        { SeatStatus.Configuring,  SeatStatus.Configuring,  "HealthCheck: recovery restored a mid-provision seat" },
        { SeatStatus.Ready,        SeatStatus.Streaming,    "SeatManager: LaunchApp" },
        { SeatStatus.Ready,        SeatStatus.Connecting,   "HealthCheck: sleep recovery" },
        { SeatStatus.Ready,        SeatStatus.Error,        "HealthCheck: session died / Apollo could not restart" },
        { SeatStatus.Ready,        SeatStatus.TearingDown,  "SeatManager: teardown" },
        { SeatStatus.Streaming,    SeatStatus.Streaming,    "SeatManager: LaunchApp on a streaming seat" },
        { SeatStatus.Streaming,    SeatStatus.Connecting,   "HealthCheck: sleep recovery mid-stream" },
        { SeatStatus.Streaming,    SeatStatus.Error,        "HealthCheck: Apollo could not restart" },
        { SeatStatus.Streaming,    SeatStatus.TearingDown,  "SeatManager: teardown of a streaming seat" },
        { SeatStatus.Connecting,   SeatStatus.Connecting,   "HealthCheck: recovery of a seat already Connecting" },
        { SeatStatus.Connecting,   SeatStatus.Ready,        "HealthCheck: recovery restored Ready" },
        { SeatStatus.Connecting,   SeatStatus.Streaming,    "HealthCheck: recovery restored Streaming" },
        { SeatStatus.Connecting,   SeatStatus.Error,        "HealthCheck: recovery failed" },
        { SeatStatus.Connecting,   SeatStatus.TearingDown,  "SeatManager: teardown of a recovering seat" },
        { SeatStatus.Error,        SeatStatus.Ready,        "SeatEndpoints: /session-reconnect (PR #22)" },
        { SeatStatus.Error,        SeatStatus.TearingDown,  "SeatManager: teardown of a failed seat" },
    };

    [Theory]
    [MemberData(nameof(RealTransitions))]
    public void EveryTransitionTheServicePerforms_IsLegal(SeatStatus from, SeatStatus to, string where)
    {
        Assert.True(from.CanTransitionTo(to), $"{from} -> {to} is performed by {where} but the table forbids it");
    }

    [Fact]
    public void StrictMode_ThrowsOnAnIllegalTransition()
    {
        // Idle -> Streaming skips the entire pipeline; nothing should ever do it.
        var seat = Seat(SeatStatus.Idle);
        var ex = Assert.Throws<InvalidOperationException>(
            () => seat.TransitionTo(SeatStatus.Streaming, NullLogger.Instance));

        Assert.Contains("Idle", ex.Message);
        Assert.Contains("Streaming", ex.Message);
        Assert.Equal(SeatStatus.Idle, seat.Status);   // rejected, so unchanged
    }

    [Fact]
    public void StrictModeIsOn_ForThisAssembly()
    {
        // Guards the ModuleInitializer above: if strict mode silently stopped being enabled, every
        // other test here would pass for the wrong reason.
        Assert.True(SeatState.StrictTransitions);
    }

    [Fact]
    public void ALegalTransition_IsApplied()
    {
        var seat = Seat(SeatStatus.Provisioning);
        seat.TransitionTo(SeatStatus.Configuring, NullLogger.Instance);
        Assert.Equal(SeatStatus.Configuring, seat.Status);
    }

    [Fact]
    public void ReAssertingTheCurrentStatus_IsAlwaysAllowed()
    {
        foreach (var status in Enum.GetValues<SeatStatus>())
        {
            var seat = Seat(status);
            seat.TransitionTo(status, NullLogger.Instance);   // must not throw in strict mode
            Assert.Equal(status, seat.Status);
        }
    }

    private sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel l, EventId e, TState s, Exception? ex,
            Func<TState, Exception?, string> f)
        { }
    }
}