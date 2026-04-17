namespace MultiSeat.Service.Display;

/// <summary>
/// Negotiates the optimal resolution and refresh rate between
/// the Moonlight client's capabilities and the host GPU's limits.
/// </summary>
public sealed class ResolutionNegotiator
{
    // Common streaming resolutions with their typical use cases
    public static readonly (int W, int H, string Label)[] CommonResolutions =
    [
        (1280, 720, "720p"),
        (1920, 1080, "1080p"),
        (2560, 1440, "1440p"),
        (3840, 2160, "4K"),
        // Handheld-specific (ROG Ally X, Steam Deck, etc.)
        (1920, 1200, "Ally X Native"),
        (1280, 800, "Deck Native"),
    ];

    public static (int Width, int Height, int Fps) Negotiate(
        int requestedW, int requestedH, int requestedFps,
        int maxEncoderWidth = 3840, int maxEncoderHeight = 2160, int maxFps = 240)
    {
        var w = Math.Min(requestedW, maxEncoderWidth);
        var h = Math.Min(requestedH, maxEncoderHeight);
        var fps = Math.Min(requestedFps, maxFps);

        // Ensure even dimensions (required by NVENC/AMF)
        w = w / 2 * 2;
        h = h / 2 * 2;

        return (w, h, fps);
    }
}
