using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using MultiSeat.Service.Interop;
using MultiSeat.Shared;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Accounts;

/// <summary>
/// Manages Windows local accounts used by MultiSeat seats.
/// Uses NetApi32 P/Invoke for account CRUD operations.
/// All MultiSeat-managed accounts are prefixed with "MultiSeatSeat" and
/// added to the Users group.
/// </summary>
public sealed class AccountManager
{
    private readonly ILogger<AccountManager> _logger;
    private readonly ConcurrentDictionary<string, AccountInfo> _managedAccounts = new(StringComparer.OrdinalIgnoreCase);

    // Secure credential store — persisted to disk via DPAPI, loaded on startup
    private readonly ConcurrentDictionary<string, string> _credentials = new(StringComparer.OrdinalIgnoreCase);

    // Persisted account store path — survives service restarts
    private static readonly string StorePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MultiSeat", "accounts.json");

    private record StoredAccount(string Username, string EncryptedPassword, bool IsManaged);

    public AccountManager(ILogger<AccountManager> logger)
    {
        _logger = logger;
        DiscoverExistingAccounts();
        LoadPersistedAccounts();
    }

    public IReadOnlyCollection<AccountInfo> ListManagedAccounts() =>
        _managedAccounts.Values.ToList().AsReadOnly();

    public bool AccountExists(string username) =>
        _managedAccounts.ContainsKey(username);

    /// <summary>
    /// Retrieve stored password for session creation. Returns null if not stored.
    /// </summary>
    public string? GetCredential(string username) =>
        _credentials.GetValueOrDefault(username);

    /// <summary>
    /// Create a new Windows local account for a MultiSeat seat.
    /// </summary>
    public AccountInfo CreateAccount(string username, string? password = null)
    {
        if (_managedAccounts.ContainsKey(username))
            throw new InvalidOperationException($"Account '{username}' already exists.");

        // Generate a strong random password if not provided
        password ??= GeneratePassword();

        var userInfo = new NetApi.UserInfo1
        {
            usri1_name = username,
            usri1_password = password,
            usri1_priv = NetApi.USER_PRIV_USER,
            usri1_flags = NetApi.UF_SCRIPT | NetApi.UF_NORMAL_ACCOUNT | NetApi.UF_DONT_EXPIRE_PASSWD,
            usri1_comment = "MultiSeat multi-seat account (managed)",
        };

        var result = NetApi.NetUserAdd(null, 1, ref userInfo, out var paramErr);

        if (result == NetApi.NERR_UserExists)
            throw new InvalidOperationException($"Windows account '{username}' already exists.");

        if (result != NetApi.NERR_Success)
            throw new InvalidOperationException(
                $"NetUserAdd failed for '{username}': error {result}, param {paramErr}");

        _logger.LogInformation("Created Windows account: {User}", username);

        // Add to Users group for interactive logon and Administrators for SudoVDA IPC
        AddToGroup(username, Constants.AccountGroup);
        AddToGroup(username, "Administrators");

        // Pre-create the user profile so the first RDP login doesn't stall
        // while Windows initializes the profile directory (can exceed the 15s timeout).
        EnsureUserProfile(username);

        // Store credential for session launching
        _credentials[username] = password;

        var account = new AccountInfo
        {
            Username = username,
            IsManaged = true
        };

        _managedAccounts.TryAdd(username, account);
        SavePersistedAccounts();
        return account;
    }

    /// <summary>
    /// Link an existing Windows local account to MultiSeat (no new account created).
    /// </summary>
    public AccountInfo LinkExistingAccount(string username, string password)
    {
        if (_managedAccounts.ContainsKey(username))
            throw new InvalidOperationException($"Account '{username}' is already linked.");

        // Verify the Windows account actually exists
        var checkResult = NetApi.NetUserGetInfo(null, username, 1, out var infoPtr);
        if (checkResult != NetApi.NERR_Success)
            throw new InvalidOperationException($"Windows account '{username}' not found (error {checkResult}).");
        NetApi.NetApiBufferFree(infoPtr);

        // Store credential for session launching
        _credentials[username] = password;

        var account = new AccountInfo
        {
            Username = username,
            IsManaged = false  // not created by MultiSeat, just linked
        };

        _managedAccounts.TryAdd(username, account);
        SavePersistedAccounts();
        _logger.LogInformation("Linked existing Windows account: {User}", username);
        return account;
    }

    /// <summary>
    /// Delete a MultiSeat-managed account or unlink an existing account.
    /// Only deletes the Windows account if it was created by MultiSeat (IsManaged=true).
    /// </summary>
    public void DeleteAccount(string username)
    {
        if (!_managedAccounts.TryGetValue(username, out var account))
            throw new InvalidOperationException($"Account '{username}' is not managed by MultiSeat.");

        if (account.IsManaged)
        {
            // Only delete the actual Windows account if MultiSeat created it
            var result = NetApi.NetUserDel(null, username);
            if (result != NetApi.NERR_Success && result != NetApi.NERR_UserNotFound)
                throw new InvalidOperationException(
                    $"NetUserDel failed for '{username}': error {result}");
            _logger.LogInformation("Deleted Windows account: {User}", username);
        }
        else
        {
            _logger.LogInformation("Unlinked existing account: {User} (Windows account preserved)", username);
        }

        _managedAccounts.TryRemove(username, out _);
        _credentials.TryRemove(username, out _);
        SavePersistedAccounts();
    }

    private void AddToGroup(string username, string groupName)
    {
        var memberInfo = new NetApi.LocalGroupMembersInfo3
        {
            lgrmi3_domainandname = $"{Environment.MachineName}\\{username}"
        };

        var result = NetApi.NetLocalGroupAddMembers(null, groupName, 3, ref memberInfo, 1);

        if (result != NetApi.NERR_Success && result != 1378 /* already member */)
        {
            _logger.LogWarning("NetLocalGroupAddMembers({User} → {Group}) failed: {Err}",
                username, groupName, result);
        }
    }

    private void DiscoverExistingAccounts()
    {
        // Scan for existing MultiSeatSeat* accounts on startup
        var resumeHandle = IntPtr.Zero;
        var result = NetApi.NetUserEnum(null, 1, 0, out var bufPtr,
            -1, out var entriesRead, out _, ref resumeHandle);

        if (result != NetApi.NERR_Success || bufPtr == IntPtr.Zero)
            return;

        try
        {
            var structSize = Marshal.SizeOf<NetApi.UserInfo1>();
            for (int i = 0; i < entriesRead; i++)
            {
                var info = Marshal.PtrToStructure<NetApi.UserInfo1>(bufPtr + i * structSize);
                if (info.usri1_name.StartsWith(Constants.AccountPrefix, StringComparison.OrdinalIgnoreCase)
                    && info.usri1_comment?.Contains("MultiSeat") == true)
                {
                    _managedAccounts.TryAdd(info.usri1_name, new AccountInfo
                    {
                        Username = info.usri1_name,
                        IsManaged = true
                    });
                }
            }

            if (_managedAccounts.Count > 0)
                _logger.LogInformation("Discovered {Count} existing MultiSeat accounts", _managedAccounts.Count);
        }
        finally
        {
            NetApi.NetApiBufferFree(bufPtr);
        }
    }

    /// <summary>
    /// Save all credentials to disk encrypted with DPAPI (machine-scope).
    /// Survives service restarts; only decryptable on this machine.
    /// </summary>
    private void SavePersistedAccounts()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            var entries = _credentials.Select(kv =>
            {
                var encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(kv.Value),
                    null, DataProtectionScope.LocalMachine);
                var isManaged = _managedAccounts.TryGetValue(kv.Key, out var acc) && acc.IsManaged;
                return new StoredAccount(kv.Key, Convert.ToBase64String(encrypted), isManaged);
            }).ToList();

            // Write-then-rename so a crash mid-write can't corrupt the credential store.
            var tmp = StorePath + ".tmp";
            File.WriteAllText(tmp,
                JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, StorePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist account credentials to {Path}", StorePath);
        }
    }

    /// <summary>
    /// Load credentials from disk on startup.
    /// Restores both credentials and linked accounts that wouldn't be found by DiscoverExistingAccounts.
    /// </summary>
    private void LoadPersistedAccounts()
    {
        if (!File.Exists(StorePath)) return;

        try
        {
            var entries = JsonSerializer.Deserialize<List<StoredAccount>>(File.ReadAllText(StorePath));
            if (entries == null) return;

            foreach (var entry in entries)
            {
                var password = Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(
                        Convert.FromBase64String(entry.EncryptedPassword),
                        null, DataProtectionScope.LocalMachine));

                _credentials[entry.Username] = password;

                // Restore linked accounts not found by DiscoverExistingAccounts
                if (!_managedAccounts.ContainsKey(entry.Username))
                {
                    _managedAccounts.TryAdd(entry.Username, new AccountInfo
                    {
                        Username = entry.Username,
                        IsManaged = entry.IsManaged
                    });
                    _logger.LogInformation(
                        "Restored {Type} account '{User}' from credential store",
                        entry.IsManaged ? "managed" : "linked", entry.Username);
                }
                else
                {
                    _logger.LogDebug("Restored credential for '{User}'", entry.Username);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load persisted accounts from {Path}", StorePath);
        }
    }

    private void EnsureUserProfile(string username)
    {
        try
        {
            var sid = new NTAccount(username).Translate(typeof(SecurityIdentifier)).ToString();
            var profilePath = new StringBuilder(260);
            var hr = UserEnv.CreateProfile(sid, username, profilePath, (uint)profilePath.Capacity);
            const int AlreadyExists = unchecked((int)0x80070050);
            if (hr == 0)
                _logger.LogInformation("Pre-created user profile for '{User}': {Path}", username, profilePath);
            else if (hr == AlreadyExists)
                _logger.LogDebug("User profile for '{User}' already exists", username);
            else
                _logger.LogWarning("CreateProfile for '{User}' returned HRESULT 0x{Hr:X8} (non-fatal)", username, hr);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to pre-create user profile for '{User}' (non-fatal)", username);
        }
    }

    private static string GeneratePassword()
    {
        // Generate a 24-char random password with mixed character classes
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
        return RandomNumberGenerator.GetString(chars, 24);
    }
}
