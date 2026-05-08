namespace MultiSeat.Shared;

/// <summary>
/// System-wide constants for port allocation, paths, and limits.
/// Tuned for Windows 11 24H2+ (build 26100+).
/// </summary>
public static class Constants
{
    // ── Port allocation ──────────────────────────────────────────────
    // Each seat reserves a block of 10 ports starting from the base.
    // Seat 0 → 47984-47993, Seat 1 → 47994-48003, etc.
    public const int PortBase = 47984;
    // Apollo uses offsets from -5 (GFE HTTPS) to +21 (RTSP) = 27-port spread.
    // 30 gives a 3-port gap between seats and avoids cross-seat collisions.
    public const int PortsPerSeat = 30;
    public const int MaxSeats = 8;

    // ── Port offsets within a seat's block ────────────────────────────
    // Matches Apollo's map_port(N) = sunshine.port + N (from network.cpp).
    public const int OffsetGfeHttps  = -5;  // GFE HTTPS — Moonlight serverinfo/pair/launch
    public const int OffsetGfeHttp   =  0;  // GFE HTTP  — same endpoints, plaintext fallback
    public const int OffsetWebUi     =  1;  // Apollo web UI HTTPS
    public const int OffsetVideo     =  9;  // RTP video stream
    public const int OffsetControl   = 10;  // ENet control channel
    public const int OffsetAudio     = 11;  // RTP audio stream
    public const int OffsetMic       = 12;  // RTP mic stream (stream_mic)
    public const int OffsetRtsp      = 26;  // RTSP session setup (TCP) — Sunshine stock RTSP port (48010 with PortBase=47984)

    // Legacy aliases kept for the ApolloConfigBuilder "port =" line (value is OffsetGfeHttp = 0)
    // and for FirewallManager until those callers are updated.
    public const int OffsetHttps = OffsetGfeHttp;   // = 0; "port =" key in sunshine.conf
    public const int OffsetHttp  = OffsetWebUi;     // = 1; web UI port

    // ── Default paths ────────────────────────────────────────────────
    public const string DefaultApolloPath = @"C:\Program Files\Apollo\sunshine.exe";
    public const string DefaultApolloConfigDir = @"C:\ProgramData\MultiSeat\apollo";
    public const string DefaultMultiSeatConfigPath = @"C:\ProgramData\MultiSeat\multiseat-host.json";

    // ── Account naming ───────────────────────────────────────────────
    public const string AccountPrefix = "MultiSeatSeat";  // e.g., MultiSeatSeat01
    public const string AccountGroup = "Users";

    // ── Windows session ──────────────────────────────────────────────
    public const int SessionConnectTimeoutMs = 15_000;
    public const int ProcessLaunchTimeoutMs = 10_000;

    // ── API ──────────────────────────────────────────────────────────
    public const string ApiKeyHeader = "X-MultiSeat-Key";
    public const int DefaultApiPort = 9550;
}
