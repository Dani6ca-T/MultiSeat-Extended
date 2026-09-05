using MultiSeat.Service.Sessions;
using MultiSeat.Service.Streaming;
using Xunit;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// G1 regression: Apollo recreates the SudoVDA monitor on (re)start, which may mint a
/// new display UUID while <c>SeatInfo.DisplayDevicePath</c> still holds the previous
/// instance's UUID. Late detection's "already known" early-out then pins the stale UUID
/// forever, and display isolation targets a ghost display.
///
/// The re-resolution decision is a pure seam pinned here; driving it through
/// <c>ApplyDisplayIsolationAsync</c> needs the real SeatManager dependency graph
/// (repo's no-fakes rule) — same rationale as <c>ResolutionChangeStillValid</c>.
/// The config write itself (<c>UpdateDisplayOutput</c>) is already pinned by
/// <c>ApolloConfigBuilder_UpdateDisplayOutput_ModifiesConfig</c>; the only new contract
/// is WHEN it runs, which the <c>Changed</c> flag below pins.
/// </summary>
public class SeatManagerDisplayIdentityTests
{
    private const string OldUuid = "{11111111-2222-3333-4444-555555555555}";
    private const string NewUuid = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";

    private static string DisplayLogJson(params string[] entries) =>
        "Info: Currently available display devices:\n[\n" +
        string.Join(",\n", entries) + "\n]\n";

    private static string DisplayEntry(
        string deviceId, string friendlyName, bool primary, int refreshNumerator) =>
        $$"""
            {
              "device_id": "{{deviceId}}",
              "display_name": "IGNORED",
              "edid": null,
              "friendly_name": "{{friendlyName}}",
              "info": {
                "hdr_state": "Disabled",
                "origin_point": { "x": 0, "y": 0 },
                "primary": {{(primary ? "true" : "false")}},
                "refresh_rate": {
                  "type": "rational",
                  "value": { "denominator": 1, "numerator": {{refreshNumerator}} }
                },
                "resolution": { "height": 1080, "width": 1920 },
                "resolution_scale": {
                  "type": "rational",
                  "value": { "denominator": 100, "numerator": 100 }
                }
              }
            }
        """;

    private static string SeatLog(string rdpId, string vddId, string vddName) =>
        DisplayLogJson(
            DisplayEntry(rdpId, "", primary: true, refreshNumerator: 1000),
            DisplayEntry(vddId, vddName, primary: false, refreshNumerator: 60));

    [Fact]
    public void StaleUuid_ReplacedByLatestValidUuid()
    {
        // After an Apollo recreation the log shows a new SudoVDA UUID while the seat
        // still carries the previous instance's one — the path must move, or isolation
        // keeps targeting the destroyed display.
        var latest = ApolloManager.ParseLatestSudoVdaDisplayIdFromLogText(
            SeatLog("{rdp-surface}", NewUuid, "VDD by MTT"));

        var (effective, changed) = SeatManager.ResolveRefreshedDisplayPath(OldUuid, latest);

        Assert.True(changed);
        Assert.Equal(NewUuid, effective);
    }

    [Fact]
    public void LatestBlockWinsOverStartupBlock()
    {
        // A seat log accumulates every enumeration for the life of the seat. The first
        // block belongs to the previous Apollo instance; only the LAST block names the
        // current display. Taking the first would pin the stale UUID by construction.
        var log = SeatLog("{rdp-surface}", OldUuid, "VDD by MTT")
            + "\nInfo: CLIENT CONNECTED\n"
            + SeatLog("{rdp-surface}", NewUuid, "VDD by MTT");

        var latest = ApolloManager.ParseLatestSudoVdaDisplayIdFromLogText(log);
        Assert.Equal(NewUuid, latest.DeviceId);

        var (effective, changed) = SeatManager.ResolveRefreshedDisplayPath(OldUuid, latest);

        Assert.True(changed);
        Assert.Equal(NewUuid, effective);
    }

    [Fact]
    public void SameUuid_NoChangeNoChurn()
    {
        // The common case: recreation kept the UUID (or nothing was recreated). The
        // decision must report no change so the caller skips the config rewrite —
        // sunshine.conf must not churn when nothing moved.
        var latest = ApolloManager.ParseLatestSudoVdaDisplayIdFromLogText(
            SeatLog("{rdp-surface}", NewUuid, "VDD by MTT"));

        var (effective, changed) = SeatManager.ResolveRefreshedDisplayPath(NewUuid, latest);

        Assert.False(changed);
        Assert.Equal(NewUuid, effective);
    }

    [Fact]
    public void NoValidDisplay_RetainsCurrentPath()
    {
        // Fail-closed: when the latest log has nothing usable (no block yet, Apollo
        // mid-startup), the known path must be kept — clearing it would skip isolation
        // that the old (possibly still valid) UUID could have applied.
        var latest = ApolloManager.ParseLatestSudoVdaDisplayIdFromLogText(
            "Info: CLIENT CONNECTED\n");

        var (effective, changed) = SeatManager.ResolveRefreshedDisplayPath(OldUuid, latest);

        Assert.False(changed);
        Assert.Equal(OldUuid, effective);
    }

    [Fact]
    public void PrimaryOnlyBlock_DoesNotReplace()
    {
        // Fail-closed selection rules are unchanged: a block whose only 1000Hz display
        // is the session primary (the RDP surface, issue #14) must not displace the
        // known path with the RDP surface's UUID.
        var log = DisplayLogJson(
            DisplayEntry("{rdp-surface}", "", primary: true, refreshNumerator: 1000));
        var latest = ApolloManager.ParseLatestSudoVdaDisplayIdFromLogText(log);
        Assert.Null(latest.DeviceId);
        Assert.True(latest.RejectedPrimaryOnly);

        var (effective, changed) = SeatManager.ResolveRefreshedDisplayPath(OldUuid, latest);

        Assert.False(changed);
        Assert.Equal(OldUuid, effective);
    }

    [Fact]
    public void UnsetPath_AdoptsLatestValidUuid()
    {
        // A seat with no known path (fresh provision race, wiped state) takes a valid
        // latest UUID rather than staying blind.
        var latest = ApolloManager.ParseLatestSudoVdaDisplayIdFromLogText(
            SeatLog("{rdp-surface}", NewUuid, "VDD by MTT"));

        var (effective, changed) = SeatManager.ResolveRefreshedDisplayPath(null, latest);

        Assert.True(changed);
        Assert.Equal(NewUuid, effective);
    }
}
