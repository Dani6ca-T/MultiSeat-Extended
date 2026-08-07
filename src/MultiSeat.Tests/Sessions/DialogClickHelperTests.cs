using MultiSeat.Service.Sessions;
using Xunit;

namespace MultiSeat.Tests.Sessions;

/// <summary>
/// Button captions carry keyboard accelerators, so comparing raw GetWindowText output never
/// matches. The mstsc security warning's affirmative button reports "Co&amp;nnect" — which is
/// why the dismisser silently failed and the dialog reached the user.
/// </summary>
public class DialogClickHelperTests
{
    [Theory]
    // The real captions, read off the live dialog 2026-08-07.
    [InlineData("Co&nnect", "Connect")]
    [InlineData("&Cancel", "Cancel")]
    [InlineData("C&lipboard", "Clipboard")]
    [InlineData("Prin&ters", "Printers")]
    [InlineData("Smart cards or Win&dows Hello for Business", "Smart cards or Windows Hello for Business")]
    public void StripAccelerators_RemovesTheAcceleratorMarker(string caption, string expected)
    {
        Assert.Equal(expected, DialogClickHelper.StripAccelerators(caption));
    }

    [Fact]
    public void StripAccelerators_TreatsDoubleAmpersandAsALiteral()
    {
        Assert.Equal("R&D", DialogClickHelper.StripAccelerators("R&&D"));
        Assert.Equal("A&B and C", DialogClickHelper.StripAccelerators("A&&B and &C"));
    }

    [Fact]
    public void StripAccelerators_DropsATrailingLoneAmpersand()
    {
        // Malformed, but reading past the end of the string would be worse.
        Assert.Equal("OK", DialogClickHelper.StripAccelerators("OK&"));
    }

    [Theory]
    [InlineData("Connect")]
    [InlineData("")]
    [InlineData("No accelerator here")]
    public void StripAccelerators_LeavesCaptionsWithoutAcceleratorsAlone(string caption)
    {
        Assert.Equal(caption, DialogClickHelper.StripAccelerators(caption));
    }

    [Fact]
    public void StripAccelerators_MakesTheRealDialogsButtonMatchable()
    {
        // The regression, stated as the thing that actually mattered: an exact compare of
        // the live caption against what the caller asks for fails, and stripping fixes it.
        const string liveCaption = "Co&nnect";
        const string whatTheCallerAsksFor = "Connect";

        Assert.NotEqual(liveCaption, whatTheCallerAsksFor);
        Assert.Equal(
            DialogClickHelper.StripAccelerators(liveCaption),
            DialogClickHelper.StripAccelerators(whatTheCallerAsksFor));
    }
}
