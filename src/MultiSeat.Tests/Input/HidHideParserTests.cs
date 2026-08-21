using MultiSeat.Service.Input;
using Xunit;

namespace MultiSeat.Tests.Input;

/// <summary>
/// Parsing HidHideCLI's output.
///
/// Every assertion here is against output copied verbatim off a real install, because the
/// previous parser was written against what the format ought to have been: it kept lines
/// STARTING WITH <c>HID\</c> or <c>USB\</c>, while the CLI emits JSON where every such path sits
/// inside a quoted value. Measured on this host: 25 non-empty lines, 0 kept, four of which did
/// contain a path. <c>ListGamingDevices()</c> therefore returned empty forever,
/// <c>CloakForSession</c> hit its "no gaming devices" early return, and the CLI was never
/// invoked at all — the second, independent reason cloaking never did anything.
///
/// The listings that are NOT JSON are worse, because they look like comments: the CLI replays its
/// own commands, so <c>--app-list</c> answers with lines beginning <c>--app-reg</c>. The old
/// reader dropped every line starting with "--", so the whitelist always read as empty too — and
/// an empty whitelist is the one reading this feature must never get wrong.
/// </summary>
public class HidHideParserTests
{
    // Verbatim output of `HidHideCLI.exe --dev-gaming --cancel` on the reference host, 2026-08-21.
    // Not hand-written: an invented fixture would only prove the parser agrees with my guess, and
    // guessing this format is what produced a parser that matched nothing for the life of the
    // project. Note the shape — the outer level is a list of CONTAINERS, each holding several
    // nodes — and that the second node is a real absent one from this host.
    private const string RealDevGamingOutput = """
 [ { "friendlyName" : "Controller (XBOX 360 For Windows)" , "devices" : [
{ "present" : true ,
"gamingDevice" : true ,
"symbolicLink" : "\\\\?\\hid#vid_045e&pid_028e&ig_00#3&8968588&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}" ,
"vendor" : "" ,
"product" : "Controller (XBOX 360 For Windows)" ,
"serialNumber" : "" ,
"usage" : "Gamepad" ,
"description" : "HID-compliant game controller" ,
"deviceInstancePath" : "HID\\VID_045E&PID_028E&IG_00\\3&8968588&0&0000" ,
"baseContainerDeviceInstancePath" : "USB\\VID_045E&PID_028E\\01" ,
"baseContainerClassGuid" : "{D61CA365-5AF4-4486-998B-9DB4734C6CA3}" ,
"baseContainerDeviceCount" : 1 } ,
{ "present" : false ,
"gamingDevice" : false ,
"symbolicLink" : "\\\\?\\hid#vid_045e&pid_028e&ig_01#3&966d8b0&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}" ,
"vendor" : "" ,
"product" : "" ,
"serialNumber" : "" ,
"usage" : "absent" ,
"description" : "HID-compliant game controller" ,
"deviceInstancePath" : "HID\\VID_045E&PID_028E&IG_01\\3&966d8b0&0&0000" ,
"baseContainerDeviceInstancePath" : "USB\\VID_045E&PID_028E\\01" ,
"baseContainerClassGuid" : "{D61CA365-5AF4-4486-998B-9DB4734C6CA3}" ,
"baseContainerDeviceCount" : 1 } ] } ]
""";

    [Fact]
    public void ParsesTheRealGamingDeviceListing()
    {
        var devices = HidHideDeviceParser.Parse(RealDevGamingOutput);

        var pad = Assert.Single(devices);
        Assert.Equal(@"HID\VID_045E&PID_028E&IG_00\3&8968588&0&0000", pad.DeviceInstancePath);
        Assert.Equal(@"USB\VID_045E&PID_028E\01", pad.BaseContainerDeviceInstancePath);
        Assert.Equal("Controller (XBOX 360 For Windows)", pad.FriendlyName);
        Assert.True(pad.Present);
        Assert.True(pad.GamingDevice);
        Assert.NotEmpty(pad.SymbolicLink);
    }

    // Both nodes have to come out of the parse, because XInput reads the XUSB one and hiding only
    // the HID node leaves the pad fully visible.
    [Fact]
    public void ARealPadYieldsBothNodes()
    {
        var pad = Assert.Single(HidHideDeviceParser.Parse(RealDevGamingOutput));

        Assert.Equal(2, pad.Nodes.Count());
        Assert.Contains(@"USB\VID_045E&PID_028E\01", pad.Nodes);
    }

    // HidHide remembers every device it has ever seen. This host's very first listing already
    // carried a dead node, and a host that has run ViGEm for a while accumulates dozens. Writing
    // rules for them is harmless; reasoning about them is not, because a phantom makes a seat look
    // like it owns a pad that does not exist.
    [Fact]
    public void AbsentNodesAreDroppedByDefaultAndKeptOnRequest()
    {
        Assert.Single(HidHideDeviceParser.Parse(RealDevGamingOutput));
        Assert.Equal(2, HidHideDeviceParser.Parse(RealDevGamingOutput, presentOnly: false).Count);
    }

    // The regression guard proper: the old filter's shape, applied to real output, keeps nothing.
    [Fact]
    public void TheOldLineStartsWithFilterWouldStillMatchNothing()
    {
        var kept = RealDevGamingOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase) ||
                        l.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(kept);
        Assert.NotEmpty(HidHideDeviceParser.Parse(RealDevGamingOutput));
    }

    // The regression this cost a live run to find. Every read carries --cloak-state so a failed
    // read can be told from an empty configuration, and the CLI answers it by replaying
    // "--cloak-off" on its own line BEFORE the JSON. Parsing the whole transcript therefore threw
    // and returned nothing, which is indistinguishable from "no gamepads" — and that is precisely
    // the confusion this class exists to end.
    //
    // The fixture above is pure JSON, so it could never have caught this. This one is what the
    // service actually receives.
    [Fact]
    public void ParsesAListingThatIsPrefixedByTheCloakStateTell()
    {
        var asRead = "--cloak-off\n" + RealDevGamingOutput;

        var pad = Assert.Single(HidHideDeviceParser.Parse(asRead));
        Assert.Equal(@"USB\VID_045E&PID_028E\01", pad.BaseContainerDeviceInstancePath);

        // and the tell itself still reads, from the same transcript
        Assert.False(HidHideDeviceParser.ParseCloakState(asRead));
    }

    [Fact]
    public void ParseSurvivesGarbageWithoutThrowing()
    {
        Assert.Empty(HidHideDeviceParser.Parse("not json at all"));
        Assert.Empty(HidHideDeviceParser.Parse(""));
        Assert.Empty(HidHideDeviceParser.Parse(" [ ] "));
    }

    // Real --app-list output from this host. HidHide whitelists its own CLI and --app-unreg on it
    // does not stick, which is why the caller filters HidHide's own binaries rather than checking
    // that the list is empty — a check that reports a problem on every host that ever installed it.
    [Fact]
    public void ParsesTheAppListThatTheOldReaderDiscardedAsComments()
    {
        const string output = """
--cloak-off
--app-reg "C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe"
--inv-off
""";

        var app = Assert.Single(HidHideDeviceParser.ParseAppList(output));
        Assert.Equal(@"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe", app);
    }

    [Fact]
    public void TheOldAppListReaderWouldHaveDiscardedEveryEntry()
    {
        const string output = """
--app-reg "C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe"
""";

        // What the previous reader did: drop anything starting with "--" as a header or comment.
        var kept = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("--"))
            .ToList();

        Assert.Empty(kept);
        Assert.Single(HidHideDeviceParser.ParseAppList(output));
    }

    // The tell that separates a failed read from an empty configuration. Without it the empty
    // output of a too-soon invocation is indistinguishable from "nothing is configured", which is
    // one step from "restoring" over entries a user wrote by hand.
    [Theory]
    [InlineData("--cloak-on", true)]
    [InlineData("--cloak-off", false)]
    public void ReadsTheCloakStateTell(string line, bool expected)
    {
        Assert.Equal(expected, HidHideDeviceParser.ParseCloakState(line + "\n--inv-off"));
    }

    [Fact]
    public void NoCloakLineMeansTheCliDidNotAnswer()
    {
        Assert.Null(HidHideDeviceParser.ParseCloakState(""));
        Assert.Null(HidHideDeviceParser.ParseCloakState("--inv-off"));
    }

    // Teardown releases what it wrote by reading rules back, so the session suffix has to survive
    // the round trip intact — and a plain hide a user added by hand must stay distinguishable.
    [Fact]
    public void HiddenDeviceListingKeepsTheSessionSuffix()
    {
        const string output = """
--cloak-on
--dev-hide "USB\VID_045E&PID_028E\01!2"
--dev-hide "HID\VID_045E&PID_028E&IG_00\3&8968588&0&0000!2"
--dev-hide "HID\VID_054C&PID_0CE6\9&SOMEONES&HAND&0000"
""";

        var hidden = HidHideDeviceParser.ParseHiddenDevices(output);

        Assert.Equal(3, hidden.Count);
        Assert.Equal(2, hidden.Count(e => HidHideSessionJail.Split(e).SessionId is not null));
        Assert.Single(hidden.Where(e => HidHideSessionJail.Split(e).SessionId is null));
    }
}
