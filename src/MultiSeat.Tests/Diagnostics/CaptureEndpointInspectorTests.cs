using MultiSeat.Service.Diagnostics;
using Xunit;

namespace MultiSeat.Tests.Diagnostics;

/// <summary>
/// `--list-capture` answers whether a session can see the capture device Apollo's stream_mic path
/// depends on. Its device match is the whole verdict, so it is the part worth pinning: Windows
/// wraps endpoint names as "Microphone (Steam Streaming Microphone)", and there is a decoy on the
/// same host — "Microphone (Steam Streaming Speakers)" — that must not be mistaken for it.
/// </summary>
public class CaptureEndpointInspectorTests
{
    [Theory]
    [InlineData("Microphone (Steam Streaming Microphone)")]
    [InlineData("Steam Streaming Microphone")]
    [InlineData("microphone (steam streaming microphone)")]   // casing varies by driver
    public void TheRealDeviceIsRecognised(string name)
    {
        Assert.True(CaptureEndpointInspector.IsSteamMic(name));
    }

    [Theory]
    [InlineData("Microphone (Steam Streaming Speakers)")]      // present on the host, NOT the mic
    [InlineData("Internal AUX Jack (Steam Streaming Speakers)")]
    [InlineData("CABLE Output (VB-Audio Virtual Cable)")]
    [InlineData("Microphone (Realtek(R) Audio)")]
    [InlineData("Voicemeeter Out A1 (VB-Audio Voicemeeter VAIO)")]
    public void OtherCaptureDevicesAreNotMistakenForIt(string name)
    {
        // All five were enumerated on the reference host in the same run as the real device. A
        // looser match — "Steam", or "Microphone" — would return a false positive on the first two
        // and report a working mic path where there is none.
        Assert.False(CaptureEndpointInspector.IsSteamMic(name));
    }

    [Fact]
    public void AnEmptyNameIsNotAMatch()
    {
        // Endpoints can come back with no friendly name; treating that as a match would make the
        // verdict depend on a device we cannot even identify.
        Assert.False(CaptureEndpointInspector.IsSteamMic(""));
    }
}
