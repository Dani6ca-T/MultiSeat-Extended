using System.Text.Json;

namespace MultiSeat.Service.Input;

/// <summary>
/// One HID node as HidHideCLI reports it.
///
/// A gamepad is not one device. A ViGEm x360 pad publishes several nodes, and the two that
/// matter here are different things:
///
///   <see cref="DeviceInstancePath"/>              the HID node   — what you would think to hide
///   <see cref="BaseContainerDeviceInstancePath"/> the XUSB node  — what XInput actually reads
///
/// Hiding only the HID node leaves the pad fully visible to XInput, which is the obvious move
/// and the wrong one. Both need a rule. Measured by @jmlopezdona in issue #19:
///
///   hidden            HID s0   XInput s0   XInput s1
///   HID node only     denied   connected   connected
///   XUSB node only    opens    empty       empty
///   XUSB with !1      opens    empty       connected
/// </summary>
public sealed record HidHideDevice
{
    public required string DeviceInstancePath { get; init; }
    public required string BaseContainerDeviceInstancePath { get; init; }
    public string FriendlyName { get; init; } = "";
    public string Product { get; init; } = "";
    public string SymbolicLink { get; init; } = "";
    public bool Present { get; init; }
    public bool GamingDevice { get; init; }

    /// <summary>
    /// Both nodes of this pad, which is what a session jail has to cover.
    /// </summary>
    public IEnumerable<string> Nodes
    {
        get
        {
            yield return DeviceInstancePath;
            if (!string.IsNullOrWhiteSpace(BaseContainerDeviceInstancePath) &&
                !BaseContainerDeviceInstancePath.Equals(DeviceInstancePath, StringComparison.OrdinalIgnoreCase))
            {
                yield return BaseContainerDeviceInstancePath;
            }
        }
    }
}

/// <summary>
/// Parses HidHideCLI's device listings.
///
/// ⚠️ The previous parser kept lines that <em>started with</em> "HID\" or "USB\". The CLI emits
/// JSON, where every such path sits inside a quoted value, so nothing ever matched: measured on
/// the reference host, 25 non-empty lines and 0 kept, 4 of which did contain a path. That made
/// <c>ListGamingDevices()</c> return empty forever, which in turn meant the CLI was never invoked
/// at all — the second, independent reason controller cloaking never did anything.
///
/// The shape, verbatim from <c>HidHideCLI --dev-gaming --cancel</c> on a real install (note the
/// outer level is a list of CONTAINERS, each holding several device nodes):
///
/// <code>
/// [ { "friendlyName" : "Controller (XBOX 360 For Windows)" , "devices" : [
///   { "present" : true ,
///     "gamingDevice" : true ,
///     "symbolicLink" : "\\\\?\\hid#vid_045e&amp;pid_028e&amp;ig_00#3&amp;8968588&amp;0&amp;0000#{4d1e55b2-...}" ,
///     "deviceInstancePath" : "HID\\VID_045E&amp;PID_028E&amp;IG_00\\3&amp;8968588&amp;0&amp;0000" ,
///     "baseContainerDeviceInstancePath" : "USB\\VID_045E&amp;PID_028E\\01" } ] } ]
/// </code>
/// </summary>
public static class HidHideDeviceParser
{
    /// <summary>
    /// Devices from a <c>--dev-gaming</c> / <c>--dev-all</c> listing.
    /// </summary>
    /// <param name="presentOnly">
    /// Drop nodes reporting <c>present: false</c>. HidHide remembers every device it has ever
    /// seen, and a host that has run ViGEm for a while accumulates dozens of dead pads — on the
    /// reference host the very first listing already carried one. Writing rules for those is
    /// harmless but reasoning about them is not: a phantom node makes a seat look like it owns a
    /// pad that does not exist.
    /// </param>
    public static List<HidHideDevice> Parse(string cliOutput, bool presentOnly = true)
    {
        var devices = new List<HidHideDevice>();
        if (string.IsNullOrWhiteSpace(cliOutput)) return devices;

        // ⚠️ The output is NOT pure JSON. Every read carries --cloak-state, and the CLI answers
        // that by replaying "--cloak-off" / "--cloak-on" on its own line before the listing — so
        // handing the whole transcript to a JSON parser throws and yields nothing, which looks
        // exactly like "no gamepads". Caught on a live host: the CLI reported a pad in the same
        // second this returned zero. Take the array and nothing else.
        var start = cliOutput.IndexOf('[');
        var end = cliOutput.LastIndexOf(']');
        if (start < 0 || end <= start) return devices;

        var json = cliOutput[start..(end + 1)];

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // Not parseable as JSON. Returning empty here would be indistinguishable from "no
            // devices", which is exactly the confusion this class exists to end — the caller
            // separates the two by whether the read reported a cloak state at all.
            return devices;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return devices;

            foreach (var container in doc.RootElement.EnumerateArray())
            {
                var friendly = GetString(container, "friendlyName");

                if (!container.TryGetProperty("devices", out var nodes) ||
                    nodes.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var node in nodes.EnumerateArray())
                {
                    var path = GetString(node, "deviceInstancePath");
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    var present = GetBool(node, "present");
                    if (presentOnly && !present) continue;

                    devices.Add(new HidHideDevice
                    {
                        DeviceInstancePath = path,
                        BaseContainerDeviceInstancePath = GetString(node, "baseContainerDeviceInstancePath"),
                        FriendlyName = friendly,
                        Product = GetString(node, "product"),
                        SymbolicLink = GetString(node, "symbolicLink"),
                        Present = present,
                        GamingDevice = GetBool(node, "gamingDevice")
                    });
                }
            }
        }

        return devices;
    }

    /// <summary>
    /// Whitelisted application paths from an <c>--app-list</c> listing.
    ///
    /// ⚠️ Not JSON, and not bare paths either — the CLI replays its own commands:
    /// <c>--app-reg "C:\Program Files\...\HidHideCLI.exe"</c>. The previous reader dropped every
    /// line beginning with "--" as a comment, so it too always returned empty, and an empty
    /// whitelist is the one reading this feature must never get wrong.
    /// </summary>
    public static List<string> ParseAppList(string cliOutput)
    {
        var apps = new List<string>();
        if (string.IsNullOrWhiteSpace(cliOutput)) return apps;

        foreach (var raw in cliOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!raw.StartsWith("--app-reg", StringComparison.OrdinalIgnoreCase)) continue;

            var first = raw.IndexOf('"');
            var last = raw.LastIndexOf('"');
            if (first < 0 || last <= first) continue;

            apps.Add(raw.Substring(first + 1, last - first - 1));
        }

        return apps;
    }

    /// <summary>
    /// Cloak state from a <c>--cloak-state</c> listing: it replays <c>--cloak-on</c> or
    /// <c>--cloak-off</c>. Null means the CLI did not answer.
    ///
    /// This is the tell that separates a FAILED read from an empty configuration, and every
    /// batched read includes <c>--cloak-state</c> for exactly that reason. Back-to-back CLI
    /// invocations come back empty, and an empty answer otherwise reads identically to "nothing
    /// is configured" — which is one step away from "restoring" over entries a user wrote.
    /// </summary>
    public static bool? ParseCloakState(string cliOutput)
    {
        if (string.IsNullOrWhiteSpace(cliOutput)) return null;

        foreach (var raw in cliOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.Equals("--cloak-on", StringComparison.OrdinalIgnoreCase)) return true;
            if (raw.Equals("--cloak-off", StringComparison.OrdinalIgnoreCase)) return false;
        }

        return null;
    }

    /// <summary>
    /// Hidden device instance paths from a <c>--dev-list</c> listing, which replays
    /// <c>--dev-hide "PATH"</c> lines. Session suffixes are preserved verbatim.
    /// </summary>
    public static List<string> ParseHiddenDevices(string cliOutput)
    {
        var hidden = new List<string>();
        if (string.IsNullOrWhiteSpace(cliOutput)) return hidden;

        foreach (var raw in cliOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!raw.StartsWith("--dev-hide", StringComparison.OrdinalIgnoreCase)) continue;

            var first = raw.IndexOf('"');
            var last = raw.LastIndexOf('"');
            if (first < 0 || last <= first) continue;

            hidden.Add(raw.Substring(first + 1, last - first - 1));
        }

        return hidden;
    }

    private static string GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    private static bool GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
