using Microsoft.Extensions.Configuration;
using MultiSeat.Service.Configuration;
using MultiSeat.Service.Input;
using Xunit;

namespace MultiSeat.Tests.Input;

/// <summary>
/// The HidHide session jail: append <c>!&lt;sessionId&gt;</c> to a blacklist entry and the device
/// is visible only in that session. Undocumented — no README, no CLI help, no release note — so
/// the format is pinned here rather than left to be re-derived.
///
/// From HidHide's own <c>Logic.c:817</c>, byte-identical in 1.4.181.0 and 1.5.230.0:
///
///   <c>sessionId != 0 &amp;&amp; jailSessionId != 0 &amp;&amp; sessionId == jailSessionId ? FALSE : TRUE</c>
///
/// Reported by @jmlopezdona in issue #19.
/// </summary>
public class HidHideSessionJailTests
{
    [Fact]
    public void ConfineAppendsTheSessionSuffixDirectlyToThePath()
    {
        Assert.Equal(@"USB\VID_045E&PID_028E\01!2",
            HidHideSessionJail.Confine(@"USB\VID_045E&PID_028E\01", 2));
    }

    // The driver requires jailSessionId != 0. A rule written for session 0 never matches, which
    // does NOT mean it is ignored - an unmatched blacklist entry is a plain global hide, so the
    // pad would vanish from the seat as well. Failing loudly beats writing that rule.
    [Fact]
    public void SessionZeroIsRefusedBecauseItWouldHideThePadEverywhere()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HidHideSessionJail.Confine(@"USB\VID_045E&PID_028E\01", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HidHideSessionJail.Confine(@"USB\VID_045E&PID_028E\01", -1));
    }

    // A pad is not one device. XInput reads the XUSB (base container) node, so hiding only the
    // HID node - the obvious move, and what this project did - leaves the pad fully visible.
    [Fact]
    public void ConfineAllCoversBothTheHidNodeAndTheXusbNode()
    {
        var pad = new HidHideDevice
        {
            DeviceInstancePath = @"HID\VID_045E&PID_028E&IG_00\3&8968588&0&0000",
            BaseContainerDeviceInstancePath = @"USB\VID_045E&PID_028E\01"
        };

        var rules = HidHideSessionJail.ConfineAll(pad, 14);

        Assert.Equal(2, rules.Count);
        Assert.Contains(@"HID\VID_045E&PID_028E&IG_00\3&8968588&0&0000!14", rules);
        Assert.Contains(@"USB\VID_045E&PID_028E\01!14", rules);
    }

    [Fact]
    public void ADeviceWithNoSeparateContainerYieldsOneRule()
    {
        var pad = new HidHideDevice
        {
            DeviceInstancePath = @"HID\VID_045E&PID_028E&IG_00\3&8968588&0&0000",
            BaseContainerDeviceInstancePath = ""
        };

        Assert.Single(HidHideSessionJail.ConfineAll(pad, 3));
    }

    // Reading rules back is how teardown releases exactly what it wrote and nothing a user added
    // by hand, so the split has to survive the '&' and '\' that fill these paths.
    [Fact]
    public void SplitRecoversThePathAndTheSession()
    {
        var (path, session) = HidHideSessionJail.Split(@"USB\VID_045E&PID_028E\01!2");

        Assert.Equal(@"USB\VID_045E&PID_028E\01", path);
        Assert.Equal(2, session);
    }

    [Fact]
    public void SplitReportsNoSessionForAPlainGlobalHide()
    {
        var (path, session) = HidHideSessionJail.Split(@"USB\VID_045E&PID_028E\01");

        Assert.Equal(@"USB\VID_045E&PID_028E\01", path);
        Assert.Null(session);
    }

    [Theory]
    [InlineData(@"USB\VID_045E&PID_028E\01!")]      // trailing bang, no session
    [InlineData(@"USB\VID_045E&PID_028E\01!x")]     // not a number
    public void SplitLeavesMalformedSuffixesAlone(string entry)
    {
        var (path, session) = HidHideSessionJail.Split(entry);

        Assert.Equal(entry, path);
        Assert.Null(session);
    }

    [Fact]
    public void ConfineAndSplitRoundTrip()
    {
        const string path = @"HID\VID_045E&PID_028E&IG_00\3&8968588&0&0000";

        var (back, session) = HidHideSessionJail.Split(HidHideSessionJail.Confine(path, 14));

        Assert.Equal(path, back);
        Assert.Equal(14, session);
    }

    // The shipped appsettings.json outranks the C# default, so the two disagreeing is a silent
    // behaviour change rather than a compile error - the same trap that made AudioMode's default
    // need a test of its own. This feature writes into a kernel-side, persistent blacklist and can
    // take a controller away from whoever is holding it, so "off unless asked for" has to be true
    // in both places at once.
    [Fact]
    public void TheShippedDefaultsMatchTheCompiledOnesAndAreOff()
    {
        var shipped = new ConfigurationBuilder()
            .AddJsonFile("service-appsettings.json", optional: false)
            .Build()
            .GetSection("MultiSeat")
            .Get<MultiSeatOptions>() ?? new MultiSeatOptions();

        var compiled = new MultiSeatOptions();

        Assert.False(compiled.EnableHidHideCloaking);
        Assert.False(compiled.EnablePadRulePreWrite);

        Assert.Equal(compiled.EnableHidHideCloaking, shipped.EnableHidHideCloaking);
        Assert.Equal(compiled.EnablePadRulePreWrite, shipped.EnablePadRulePreWrite);
        Assert.Equal(compiled.VerifyHidHideJail, shipped.VerifyHidHideJail);
        Assert.Empty(shipped.SeatPadDevicePaths);
    }
}
