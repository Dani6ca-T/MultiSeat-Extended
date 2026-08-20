using MultiSeat.Service.Input;
using Xunit;

namespace MultiSeat.Tests.Input;

/// <summary>
/// Guards the HidHideCLI argument forms.
///
/// Every one of these was wrong, at eleven call sites, for the life of the project: the code
/// passed "--dev-hide --id &lt;path&gt;" and "--app-reg --path &lt;exe&gt;", neither of which HidHideCLI
/// accepts. It takes the value directly after the switch. So HidHide received the literal
/// string "--id" as a device instance path, matched nothing, exited 0, and controller cloaking
/// was a silent no-op on every host while HidHide sat installed and healthy.
///
/// Nothing caught it because nothing asserted on the arguments — the CLI reports success either
/// way. Reported by @jmlopezdona in issue #19 and confirmed against HidHideCLI --help.
/// </summary>
public class HidHideArgumentTests
{
    // The two forms that must never come back. A test that only checked the good form would
    // pass against "--dev-hide --id \"X\"" too, since that contains "--dev-hide" as well.
    [Theory]
    [InlineData("--id")]
    [InlineData("--path")]
    public void NoBuilderEmitsTheInventedSwitches(string invented)
    {
        var built = new[]
        {
            HidHideConfigurator.HideDeviceArgs(@"HID\VID_045E&PID_028E\7&1234&0&0000"),
            HidHideConfigurator.UnhideDeviceArgs(@"HID\VID_045E&PID_028E\7&1234&0&0000"),
            HidHideConfigurator.RegisterAppArgs(@"C:\Program Files\ApolloVibe\sunshine.exe"),
            HidHideConfigurator.UnregisterAppArgs(@"C:\Program Files\ApolloVibe\sunshine.exe"),
            HidHideConfigurator.ListGamingDevicesArgs,
            HidHideConfigurator.ListAppsArgs,
        };

        Assert.All(built, args => Assert.DoesNotContain(invented, args));
    }

    // The value must sit directly after the switch, quoted - that is the whole contract.
    [Fact]
    public void DeviceBuildersPutTheQuotedPathDirectlyAfterTheSwitch()
    {
        const string device = @"HID\VID_045E&PID_028E\7&1234&0&0000";

        Assert.Equal($"--dev-hide \"{device}\"", HidHideConfigurator.HideDeviceArgs(device));
        Assert.Equal($"--dev-unhide \"{device}\"", HidHideConfigurator.UnhideDeviceArgs(device));
    }

    [Fact]
    public void AppBuildersPutTheQuotedPathDirectlyAfterTheSwitch()
    {
        const string exe = @"C:\Program Files\ApolloVibe\sunshine.exe";

        Assert.Equal($"--app-reg \"{exe}\"", HidHideConfigurator.RegisterAppArgs(exe));
        Assert.Equal($"--app-unreg \"{exe}\"", HidHideConfigurator.UnregisterAppArgs(exe));
    }

    // Paths with spaces are the normal case here - "Program Files" - so the quoting is not
    // decoration. Without it HidHide would receive a truncated path and, again, match nothing.
    [Fact]
    public void PathsWithSpacesStayQuoted()
    {
        var args = HidHideConfigurator.RegisterAppArgs(@"C:\Program Files\ApolloVibe\sunshine.exe");

        Assert.StartsWith("--app-reg \"", args);
        Assert.EndsWith("\"", args);
    }

    // HidHideCLI saves its configuration on exit even when the invocation only listed
    // something, so a read that omits --cancel rewrites the config it was asked to report on.
    [Theory]
    [InlineData(HidHideConfigurator.ListGamingDevicesArgs)]
    [InlineData(HidHideConfigurator.ListAppsArgs)]
    public void ReadOnlyQueriesCancelInsteadOfSaving(string args)
    {
        Assert.Contains("--cancel", args);
    }
}
