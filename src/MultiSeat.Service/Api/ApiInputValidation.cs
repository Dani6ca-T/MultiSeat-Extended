using System.Text.RegularExpressions;

namespace MultiSeat.Service.Api;

/// <summary>
/// Input validation helpers for API endpoints.
/// Centralises rules so every endpoint uses the same constraints.
/// </summary>
internal static class ApiInputValidation
{
    // Windows usernames: start with alphanumeric, may contain . _ -
    // Max 20 chars to keep directory names sane.
    private static readonly Regex AccountNameRegex =
        new(@"^[a-zA-Z0-9][a-zA-Z0-9._\-]{0,19}$", RegexOptions.Compiled);

    /// <summary>
    /// Returns true if the account name is safe to use in file paths and shell commands.
    /// Rejects traversal sequences, shell metacharacters, and names that are too long.
    /// </summary>
    public static bool IsValidAccountName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && AccountNameRegex.IsMatch(name);

    public static IResult AccountNameError() =>
        Results.BadRequest(new
        {
            error = "Invalid account name. Use 1–20 alphanumeric characters, dots, underscores, or hyphens. Must start with a letter or digit."
        });
}
