namespace MultiSeat.Service.Api;

/// <summary>
/// Holds the live API authentication toggle — checked on every request so
/// enabling/disabling auth takes effect immediately without a service restart.
/// Persisted to appsettings.json via the /api/system/auth endpoint.
/// </summary>
public sealed class ApiAuthState
{
    private volatile bool _enabled;

    public ApiAuthState(bool enabled, string apiKey)
    {
        _enabled = enabled;
        ApiKey = apiKey;
    }

    public bool IsEnabled => _enabled;

    /// <summary>The resolved API key (auto-generated or configured). Never changes at runtime.</summary>
    public string ApiKey { get; }

    public void SetEnabled(bool enabled) => _enabled = enabled;
}
