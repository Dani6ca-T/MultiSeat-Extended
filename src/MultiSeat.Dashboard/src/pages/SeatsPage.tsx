import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import type { AccountInfo } from "../api/types";
import { accounts as accountsApi } from "../api/client";
import { useSeats } from "../hooks/useSeats";
import { SeatCard } from "../components/SeatCard";
import { CreateSeatForm } from "../components/CreateSeatForm";
import { ConnectionBar } from "../components/ConnectionBar";

export function SeatsPage() {
  const { seats, connected, refresh } = useSeats();
  const [accounts, setAccounts] = useState<AccountInfo[]>([]);
  const [showCreate, setShowCreate] = useState(false);

  const loadAccounts = useCallback(async () => {
    try {
      setAccounts(await accountsApi.list());
    } catch {
      // accounts may not be accessible
    }
  }, []);

  useEffect(() => {
    loadAccounts();
  }, [loadAccounts]);

  const activeSeats = seats.filter(
    (s) => s.status !== "Idle"
  );

  return (
    <div>
      <ConnectionBar connected={connected} onReconnect={refresh} />

      <div className="page-header">
        <div>
          <h2>Seats</h2>
          <span className="text-muted">
            {activeSeats.length} active
          </span>
        </div>
        <button onClick={() => setShowCreate(!showCreate)}>
          {showCreate ? "Cancel" : "+ New Seat"}
        </button>
      </div>

      {showCreate && (
        <CreateSeatForm
          accounts={accounts}
          onCreated={() => {
            setShowCreate(false);
            refresh();
          }}
        />
      )}

      {seats.length === 0 ? (
        <div className="empty-state">
          <p>No seats provisioned yet.</p>
          <p className="text-muted" style={{ marginBottom: 20 }}>
            Create an account first, then use "+ New Seat" to start streaming.
          </p>
          <Link to="/accounts" className="btn-link" style={{ padding: "8px 20px" }}>
            Go to Accounts →
          </Link>
        </div>
      ) : (
        <div className="card-grid">
          {seats.map((seat) => (
            <SeatCard key={seat.id} seat={seat} onUpdate={refresh} />
          ))}
        </div>
      )}
    </div>
  );
}
