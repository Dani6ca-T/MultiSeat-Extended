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
    public const int PortsPerSeat = 10;
    public const int MaxSeats = 8;

    // ── Port offsets within a seat's block ────────────────────────────
    public const int OffsetHttps = 0;   // Apollo HTTPS (pairing)
    public const int OffsetHttp = 1;    // Apollo HTTP
    public const int OffsetVideo = 2;   // RTP video
    public const int OffsetAudio = 3;   // RTP audio
    public const int OffsetControl = 4; // Control channel

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
