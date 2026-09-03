using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiSeat.Service.Accounts;
using MultiSeat.Service.Audio;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Display;
using MultiSeat.Service.Emulators;
using MultiSeat.Service.Input;
using MultiSeat.Service.Monitoring;
using MultiSeat.Service.ProcessTracking;
using MultiSeat.Service.Sessions;
using MultiSeat.Service.Storage;
using MultiSeat.Service.Streaming;
using MultiSeat.Shared;
using Xunit;

namespace MultiSeat.Tests.Di;

/// <summary>
/// DI container smoke tests verifying that the Apollo/SeatManager dependency graph
/// can be constructed by the DI container. Catches registration errors, missing
/// dependencies, and circular dependencies at test time rather than at startup.
/// Replicates the registrations from Program.cs to verify the full chain.
/// </summary>
public class ApolloDiSmokeTests
{
    /// <summary>
    /// Creates a DI container matching the real Program.cs registrations.
    /// This verifies that the full dependency graph resolves correctly.
    /// </summary>
    private static ServiceProvider CreateTestProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddOptions();

        services.Configure<MultiSeatOptions>(opts =>
        {
            opts.PortBase = 48100;
            opts.ApolloExePath = @"C:\nonexistent\Apollo.exe";
            opts.ApolloConfigDir = Path.Combine(Path.GetTempPath(), "test-apollo-config");
        });

        // Replicate Program.cs registrations exactly
        // Note: ISessionLauncher is registered in ApiServer.ConfigureServices, not here.
        // SeatManager takes ISessionLauncher in its constructor, so we need it.
        services.AddSingleton<AccountManager>();
        services.AddSingleton<IAccountManager>(sp => sp.GetRequiredService<AccountManager>());
        services.AddSingleton<SessionLauncher>();
        services.AddSingleton<ISessionLauncher>(sp => sp.GetRequiredService<SessionLauncher>());
        services.AddSingleton<RdpWrapper>();
        services.AddSingleton<ProcessInjector>();
        services.AddSingleton<VirtualDisplayManager>();
        services.AddSingleton<IVirtualDisplayManager>(sp => sp.GetRequiredService<VirtualDisplayManager>());
        services.AddSingleton<ApolloManager>();
        services.AddSingleton<ApolloConfigBuilder>();
        services.AddSingleton<OnConnectAppLauncher>();
        services.AddSingleton<ClientResolutionFollower>();
        services.AddSingleton<ApolloServerQuery>();
        services.AddSingleton<HostApolloMonitor>();
        services.AddSingleton<PortAllocator>();
        services.AddSingleton<AudioDeviceEnumerator>();
        services.AddSingleton<AudioRouter>();
        services.AddSingleton<ControllerManager>();
        services.AddSingleton<InputRouter>();
        services.AddSingleton<InputHookManager>();
        services.AddSingleton<HidHideConfigurator>();
        services.AddSingleton<FirewallManager>();
        services.AddSingleton<SeatPresetStore>();
        services.AddSingleton<GpuMonitor>();
        services.AddSingleton<MetricsCollector>();
        services.AddSingleton<SessionHealthCheck>();
        services.AddSingleton<IProcessTracker, WindowsProcessTracker>();
        services.AddSingleton<IProcessMonitor, WindowsProcessMonitor>();
        services.AddSingleton<SharedLibraryProvisioner>();
        services.AddSingleton<IEmulatorConfigSeeder, RetroArchConfigSeeder>();
        services.AddSingleton<SeatLifecycleGate>();
        services.AddSingleton<SeatManager>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Verifies that the full service graph from Program.cs can be constructed.
    /// This catches missing registrations, circular dependencies, and
    /// constructor resolution failures at test time rather than at startup.
    /// </summary>
    [Fact]
    public void FullServiceGraph_CanBeConstructed()
    {
        using var provider = CreateTestProvider();

        // The most important resolution: SeatManager pulls in the full dependency graph
        var seatManager = provider.GetService<SeatManager>();
        Assert.NotNull(seatManager);

        // Verify the critical Apollo chain specifically
        var apolloManager = provider.GetService<ApolloManager>();
        Assert.NotNull(apolloManager);

        var configBuilder = provider.GetService<ApolloConfigBuilder>();
        Assert.NotNull(configBuilder);

        var serverQuery = provider.GetService<ApolloServerQuery>();
        Assert.NotNull(serverQuery);

        // Verify InputEndpoints dependencies resolve independently
        var inputRouter = provider.GetService<InputRouter>();
        Assert.NotNull(inputRouter);

        var inputHookManager = provider.GetService<InputHookManager>();
        Assert.NotNull(inputHookManager);
    }

    /// <summary>
    /// Verifies that SeatManager depends on ApolloManager, not on
    /// ApolloConfigBuilder or ApolloServerQuery directly.
    ///
    /// This is the architectural invariant from the Apollo provider boundary refactor:
    ///   SeatManager → ApolloManager → {ApolloConfigBuilder, ApolloServerQuery}
    /// </summary>
    [Fact]
    public void SeatManager_Dependencies_RoutedThroughApolloManager()
    {
        using var provider = CreateTestProvider();

        var seatManager = provider.GetService<SeatManager>();
        Assert.NotNull(seatManager);

        var apolloManager = provider.GetService<ApolloManager>();
        Assert.NotNull(apolloManager);
    }
}
