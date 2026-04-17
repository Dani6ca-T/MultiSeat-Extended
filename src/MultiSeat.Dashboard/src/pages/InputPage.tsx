import { useCallback, useState } from "react";
import { input } from "../api/client";
import { usePolling } from "../hooks/usePolling";
import { useSeats } from "../hooks/useSeats";
import type { ControllerInfo } from "../api/types";

export function InputPage() {
  const controllerFetcher = useCallback(() => input.controllers(), []);
  const hookFetcher = useCallback(() => input.hookStatus(), []);

  const {
    data: controllers,
    loading: controllersLoading,
    refresh: refreshControllers,
  } = usePolling(controllerFetcher, 2000);
  const { data: hookStatus } = usePolling(hookFetcher, 3000);
  const { seats } = useSeats();
  const [assigning, setAssigning] = useState(false);

  const activeSeats = seats.filter(
    (s) => s.status === "Ready" || s.status === "Streaming"
  );

  const handleAutoAssign = async () => {
    setAssigning(true);
    try {
      await input.autoAssign();
      refreshControllers();
    } catch (e) {
      alert(e instanceof Error ? e.message : "Auto-assign failed");
    } finally {
      setAssigning(false);
    }
  };

  const handleAssign = async (ctrl: ControllerInfo, seatId: string) => {
    try {
      if (seatId) {
        await input.assign(ctrl.xInputIndex, seatId);
      } else {
        await input.unassign(ctrl.xInputIndex);
      }
      refreshControllers();
    } catch (e) {
      alert(e instanceof Error ? e.message : "Assignment failed");
    }
  };

  return (
    <div>
      <div className="page-header">
        <h2>Input Devices</h2>
        <button onClick={handleAutoAssign} disabled={assigning}>
          {assigning ? "Assigning..." : "Auto-Assign All"}
        </button>
      </div>

      {/* Hook Status */}
      <div className="card" style={{ marginBottom: 16 }}>
        <div className="card-body">
          <h3>Keyboard / Mouse Isolation</h3>
          <div className="stat-grid" style={{ marginTop: 8 }}>
            <div className="stat-item">
              <span className="stat-label">Hooks</span>
              <span className="stat-value">
                {hookStatus?.installed ? (
                  <span style={{ color: "var(--success)" }}>Installed</span>
                ) : (
                  <span style={{ color: "var(--text-secondary)" }}>
                    Not installed
                  </span>
                )}
              </span>
            </div>
            <div className="stat-item">
              <span className="stat-label">Target Session</span>
              <span className="stat-value">
                {hookStatus?.enabled
                  ? `Session #${hookStatus.targetSessionId}`
                  : "None"}
              </span>
            </div>
          </div>
          {!hookStatus?.installed && (
            <p
              className="text-muted"
              style={{ fontSize: 12, marginTop: 8 }}
            >
              Keyboard/mouse hooks activate when a seat is provisioned.
              Ensure MultiSeatInputHook.dll is in the service directory.
            </p>
          )}
        </div>
      </div>

      {/* Controllers */}
      <h3 style={{ marginBottom: 12 }}>XInput Controllers</h3>

      {controllersLoading ? (
        <div className="text-muted">Scanning controllers...</div>
      ) : !controllers || controllers.length === 0 ? (
        <div className="empty-state">
          <p>No controllers detected.</p>
          <p className="text-muted">
            Connect an Xbox or compatible XInput controller to assign it to a
            seat.
          </p>
        </div>
      ) : (
        <div className="card-grid">
          {controllers.map((ctrl) => (
            <div className="card" key={ctrl.xInputIndex}>
              <div className="card-header">
                <div>
                  <h3 style={{ margin: 0 }}>
                    Controller {ctrl.xInputIndex + 1}
                  </h3>
                  <span className="text-muted" style={{ fontSize: 12 }}>
                    XInput #{ctrl.xInputIndex}
                  </span>
                </div>
                <span
                  style={{
                    width: 10,
                    height: 10,
                    borderRadius: "50%",
                    backgroundColor: "var(--success)",
                    display: "inline-block",
                  }}
                  title="Connected"
                />
              </div>
              <div className="card-body">
                <label>
                  Assigned Seat
                  <select
                    value={ctrl.assignedSeatId ?? ""}
                    onChange={(e) => handleAssign(ctrl, e.target.value)}
                  >
                    <option value="">Unassigned</option>
                    {activeSeats.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.accountName} (Session #{s.sessionId})
                      </option>
                    ))}
                  </select>
                </label>
                {ctrl.assignedSeatName && (
                  <div
                    className="text-muted"
                    style={{ fontSize: 12, marginTop: 4 }}
                  >
                    Routing to: {ctrl.assignedSeatName}
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Unconnected slots */}
      {controllers && controllers.length < 4 && (
        <div
          className="text-muted"
          style={{ fontSize: 12, marginTop: 16 }}
        >
          {4 - controllers.length} XInput slot
          {4 - controllers.length !== 1 ? "s" : ""} available (max 4)
        </div>
      )}
    </div>
  );
}
