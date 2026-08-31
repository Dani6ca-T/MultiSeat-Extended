using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Accounts;

/// <summary>
/// Manages Windows local accounts used by MultiSeat seats.
/// Abstracts account CRUD, credential storage, and group membership management.
///
/// The concrete implementation (AccountManager) uses NetApi32 P/Invoke, DPAPI,
/// and SecurityIdentifier — none of which leak through this interface.
/// All types are primitives or <see cref="AccountInfo"/> from MultiSeat.Shared.
/// </summary>
public interface IAccountManager
{
    /// <summary>List all accounts tracked by MultiSeat (both managed and linked).</summary>
    IReadOnlyCollection<AccountInfo> ListManagedAccounts();

    /// <summary>Check whether a username is tracked by MultiSeat.</summary>
    bool AccountExists(string username);

    /// <summary>Retrieve the stored password for session creation. Returns null if not stored.</summary>
    string? GetCredential(string username);

    /// <summary>Create a new Windows local account for a MultiSeat seat.</summary>
    AccountInfo CreateAccount(string username, string? password = null);

    /// <summary>Link an existing Windows local account to MultiSeat (no new account created).</summary>
    AccountInfo LinkExistingAccount(string username, string password);

    /// <summary>Delete a MultiSeat-managed account or unlink an existing account.</summary>
    void DeleteAccount(string username);

    /// <summary>
    /// Put a seat account in the groups a seat needs (Users + Remote Desktop Users)
    /// and remove it from Administrators if not explicitly granted.
    /// </summary>
    void ApplySeatGroupMembership(string username);

    /// <summary>
    /// Bring every MultiSeat-created account's group membership in line with the
    /// current policy. Called at service startup.
    /// </summary>
    void NormalizeManagedAccountPrivileges();
}
