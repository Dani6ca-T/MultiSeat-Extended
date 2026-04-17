import { useCallback, useEffect, useState } from "react";
import type { AccountInfo } from "../api/types";
import { accounts as accountsApi } from "../api/client";

export function AccountsPage() {
  const [accounts, setAccounts] = useState<AccountInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Create/Link form state
  const [showCreate, setShowCreate] = useState(false);
  const [showLink, setShowLink] = useState(false);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [creating, setCreating] = useState(false);

  const loadAccounts = useCallback(async () => {
    try {
      const list = await accountsApi.list();
      setAccounts(list);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load accounts");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadAccounts();
  }, [loadAccounts]);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!username.trim()) return;

    setCreating(true);
    try {
      await accountsApi.create({ username: username.trim(), password });
      setUsername("");
      setPassword("");
      setShowCreate(false);
      loadAccounts();
    } catch (e) {
      alert(e instanceof Error ? e.message : "Failed to create account");
    } finally {
      setCreating(false);
    }
  };

  const handleLink = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!username.trim() || !password) return;

    setCreating(true);
    try {
      await accountsApi.link({ username: username.trim(), password });
      setUsername("");
      setPassword("");
      setShowLink(false);
      loadAccounts();
    } catch (e) {
      alert(e instanceof Error ? e.message : "Failed to link account");
    } finally {
      setCreating(false);
    }
  };

  const handleDelete = async (name: string) => {
    if (!confirm(`Delete account "${name}"? This cannot be undone.`)) return;
    try {
      await accountsApi.destroy(name);
      loadAccounts();
    } catch (e) {
      alert(e instanceof Error ? e.message : "Failed to delete account");
    }
  };

  if (loading) return <div className="text-muted">Loading accounts...</div>;

  return (
    <div>
      <div className="page-header">
        <h2>Accounts</h2>
        <div style={{ display: "flex", gap: 8 }}>
          <button onClick={() => { setShowLink(true); setShowCreate(false); setUsername(""); setPassword(""); }}>
            Link Existing
          </button>
          <button onClick={() => { setShowCreate(true); setShowLink(false); setUsername(""); setPassword(""); }}>
            + New Account
          </button>
        </div>
      </div>

      {showLink && (
        <form onSubmit={handleLink} className="create-form">
          <h3>Link Existing Windows Account</h3>
          <p className="text-muted" style={{ marginBottom: 12 }}>
            Use an existing Windows account for streaming. The account will not be modified.
          </p>
          <label>
            Windows Username
            <input
              type="text"
              placeholder="Windows username"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
            />
          </label>
          <label>
            Password
            <input
              type="password"
              placeholder="Windows account password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </label>
          <div style={{ display: "flex", gap: 8 }}>
            <button type="submit" disabled={creating || !username.trim() || !password}>
              {creating ? "Linking..." : "Link Account"}
            </button>
            <button type="button" onClick={() => setShowLink(false)}>Cancel</button>
          </div>
        </form>
      )}

      {showCreate && (
        <form onSubmit={handleCreate} className="create-form">
          <h3>Create New Account</h3>
          <label>
            Username
            <input
              type="text"
              placeholder="MultiSeatSeat01"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
            />
          </label>
          <label>
            Password
            <input
              type="password"
              placeholder="Leave blank for auto-generated"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </label>
          <div style={{ display: "flex", gap: 8 }}>
            <button type="submit" disabled={creating || !username.trim()}>
              {creating ? "Creating..." : "Create Account"}
            </button>
            <button type="button" onClick={() => setShowCreate(false)}>Cancel</button>
          </div>
        </form>
      )}

      {error && <div className="error-banner">{error}</div>}

      {accounts.length === 0 ? (
        <div className="empty-state">
          <p>No managed accounts.</p>
          <p className="text-muted">
            Create a Windows account to use as a streaming seat.
          </p>
        </div>
      ) : (
        <div className="data-table-wrap">
          <table className="data-table">
            <thead>
              <tr>
                <th>Username</th>
                <th>SID</th>
                <th>Created</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {accounts.map((a) => (
                <tr key={a.username}>
                  <td>
                    <strong>{a.username}</strong>
                    {a.isManaged && (
                      <span className="badge-managed">managed</span>
                    )}
                  </td>
                  <td className="text-muted" style={{ fontSize: 12 }}>
                    {a.sid ?? "--"}
                  </td>
                  <td className="text-muted">
                    {new Date(a.createdAt).toLocaleDateString()}
                  </td>
                  <td>
                    <button
                      className="btn-danger btn-sm"
                      onClick={() => handleDelete(a.username)}
                    >
                      {a.isManaged ? "Delete" : "Unlink"}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
