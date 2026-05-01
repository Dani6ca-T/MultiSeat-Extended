import { useState, useEffect, useCallback } from "react";
import type { SeatInfo, SeatServices, NvencQualityPreset } from "../api/types";
import { seats as seatsApi } from "../api/client";
import { StatusBadge } from "./StatusBadge";

interface Props {
  seat: SeatInfo;
  onUpdate: () => void;
}

export function SeatCard({ seat, onUpdate }: Props) {
  const [launching, setLaunching] = useState(false);
  const [destroying, setDestroying] = useState(false);
  const [launchPath, setLaunchPath] = useState("");
  const [showLaunch, setShowLaunch] = useState(false);
  const [services, setServices] = useState<SeatServices | null>(null);
  const [showServices, setShowServices] = useState(false);
  const [actionLoading, setActionLoading] = useState<string | null>(null);
  const [autoStartLoading, setAutoStartLoading] = useState(false);
  const [presetLoading, setPresetLoading] = useState(false);

  const isActive = seat.status === "Ready" || seat.status === "Streaming";

  const fetchServices = useCallback(async () => {
    if (!showServices || seat.status === "Idle") return;
    try {
      const s = await seatsApi.services(seat.id);
      setServices(s);
    } catch {
      /* ignore */
    }
  }, [seat.id, seat.status, showServices]);

  useEffect(() => {
    fetchServices();
    if (!showServices) return;
    const interval = setInterval(fetchServices, 3000);
    return () => clearInterval(interval);
  }, [fetchServices, showServices]);

  const handleNvencPreset = async (preset: NvencQualityPreset) => {
    if (preset === seat.nvencPreset || presetLoading) return;
    setPresetLoading(true);
    try {
      await seatsApi.setNvencPreset(seat.id, preset);
      onUpdate();
    } catch (e) {
      alert(e instanceof Error ? e.message : "Failed to change quality preset");
    } finally {
      setPresetLoading(false);
    }
  };

  const handleAutoStart = async () => {
    setAutoStartLoading(true);
    try {
      await seatsApi.setAutoStart(seat.id, !seat.autoStart);
      onUpdate();
    } catch (e) {
      alert(e instanceof Error ? e.message : "Failed to update auto-start");
    } finally {
      setAutoStartLoading(false);
    }
  };

  const handleDestroy = async () => {
    if (!confirm(`Tear down seat ${seat.accountName}?`)) return;
    setDestroying(true);
    try {
      await seatsApi.destroy(seat.id);
      onUpdate();
    } catch (e) {
      alert(e instanceof Error ? e.message : "Failed to destroy seat");
    } finally {
      setDestroying(false);
    }
  };

  const handleLaunch = async () => {
    if (!launchPath.trim()) return;
    setLaunching(true);
    try {
      await seatsApi.launch(seat.id, { executablePath: launchPath.trim() });
      setShowLaunch(false);
      setLaunchPath("");
      onUpdate();
    } catch (e) {
      alert(e instanceof Error ? e.message : "Failed to launch app");
    } finally {
      setLaunching(false);
    }
  };

  const serviceAction = async (name: string, fn: () => Promise<unknown>) => {
    setActionLoading(name);
    try {
      await fn();
      onUpdate();
      fetchServices();
    } catch (e) {
      alert(e instanceof Error ? e.message : `Failed: ${name}`);
    } finally {
      setActionLoading(null);
    }
  };

  // Apollo 'port' config = HTTP GameStream port — this is what Moonlight "Add Host" needs.
  // portBase+1 is the HTTPS web UI port, which returns 404 for /serverinfo.
  const moonlightPort = seat.portBase > 0 ? seat.portBase : null;
  const uptime = seat.readyAt ? formatDuration(new Date(seat.readyAt)) : null;

  return (
    <div className="card">
      <div className="card-header">
        <div>
          <h3 style={{ margin: 0 }}>{seat.accountName}</h3>
          <span className="text-muted" style={{ fontSize: 12 }}>
            {seat.id.substring(0, 8)}
          </span>
        </div>
        <StatusBadge status={seat.status} />
      </div>

      <div className="card-body">
        <div className="stat-grid">
          <StatItem
            label="Session"
            value={seat.sessionId >= 0 ? `#${seat.sessionId}` : "--"}
          />
          <StatItem
            label="Resolution"
            value={`${seat.width}x${seat.height}@${seat.fps}`}
          />
          <StatItem label="Port" value={moonlightPort ? String(moonlightPort) : "--"} />
          <StatItem
            label="Apollo PID"
            value={seat.apolloProcessId > 0 ? String(seat.apolloProcessId) : "--"}
          />
          <StatItem
            label="VAC Cable"
            value={seat.vacCableIndex >= 0 ? `#${seat.vacCableIndex}` : "--"}
          />
          <StatItem
            label="Controller"
            value={seat.viGEmControllerIndex >= 0 ? `#${seat.viGEmControllerIndex}` : "--"}
          />
        </div>

        {/* Moonlight connection info */}
        {moonlightPort && isActive && (
          <MoonlightAddress host={window.location.hostname} port={moonlightPort} />
        )}

        {seat.launchApp && (
          <div className="text-muted" style={{ fontSize: 12, marginTop: 8 }}>
            App: {seat.launchApp}
          </div>
        )}

        {/* Auto-start toggle */}
        {seat.status !== "Idle" && seat.status !== "TearingDown" && (
          <div className="autostart-row">
            <span className="stat-label">Auto-start on boot</span>
            <button
              className={`toggle-btn${seat.autoStart ? " toggle-btn--on" : ""}`}
              onClick={handleAutoStart}
              disabled={autoStartLoading}
              title={seat.autoStart ? "Disable auto-start" : "Enable auto-start"}
            >
              {autoStartLoading ? "..." : seat.autoStart ? "On" : "Off"}
            </button>
          </div>
        )}

        {/* NVENC quality preset */}
        {seat.status !== "Idle" && seat.status !== "TearingDown" && (
          <div className="autostart-row">
            <span className="stat-label">Quality</span>
            <div className="preset-toggle">
              {(["Latency", "Balanced", "Quality"] as NvencQualityPreset[]).map((p) => (
                <button
                  key={p}
                  className={`preset-btn${seat.nvencPreset === p ? " preset-btn--active" : ""}`}
                  onClick={() => handleNvencPreset(p)}
                  disabled={presetLoading}
                  title={p === "Latency" ? "P1 — lowest encode latency"
                       : p === "Quality" ? "P7 — best quality, more GPU"
                       : "P4 — balanced (default)"}
                >
                  {presetLoading && seat.nvencPreset !== p ? p : p}
                </button>
              ))}
            </div>
          </div>
        )}

        {seat.errorMessage && (
          <div className="error-banner">{seat.errorMessage}</div>
        )}

        {uptime && (
          <div className="text-muted" style={{ fontSize: 12, marginTop: 4 }}>
            Uptime: {uptime}
          </div>
        )}

        {/* ── Service Management Panel ─────────────────────────── */}
        {seat.status !== "Idle" && seat.status !== "TearingDown" && (
          <div style={{ marginTop: 12 }}>
            <button
              className="btn-ghost btn-sm"
              onClick={() => setShowServices(!showServices)}
              style={{ width: "100%", textAlign: "left" }}
            >
              {showServices ? "▾" : "▸"} Manage Services
            </button>

            {showServices && services && (
              <div className="service-panel">
                <ServiceRow
                  name="Apollo"
                  active={services.apollo}
                  detail={services.apolloRestarts > 0 ? `${services.apolloRestarts} restarts` : undefined}
                  actions={
                    <>
                      {seat.portBase > 0 && (
                        <a
                          href={`https://${window.location.hostname}:${seat.portBase + 1}`}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="btn-sm btn-link"
                          title="Open Apollo Web UI"
                        >
                          Open
                        </a>
                      )}
                      {services.apollo ? (
                        <>
                          <button
                            className="btn-sm"
                            disabled={actionLoading !== null}
                            onClick={() => serviceAction("apollo-restart", () => seatsApi.apolloRestart(seat.id))}
                          >
                            {actionLoading === "apollo-restart" ? "..." : "Restart"}
                          </button>
                          <button
                            className="btn-sm btn-danger"
                            disabled={actionLoading !== null}
                            onClick={() => serviceAction("apollo-stop", () => seatsApi.apolloStop(seat.id))}
                          >
                            {actionLoading === "apollo-stop" ? "..." : "Stop"}
                          </button>
                        </>
                      ) : (
                        <button
                          className="btn-sm"
                          disabled={actionLoading !== null}
                          onClick={() => serviceAction("apollo-start", () => seatsApi.apolloStart(seat.id))}
                        >
                          {actionLoading === "apollo-start" ? "..." : "Start"}
                        </button>
                      )}
                    </>
                  }
                />
                <ServiceRow
                  name="Display"
                  active={services.display}
                  actions={
                    <button
                      className="btn-sm"
                      disabled={actionLoading !== null}
                      onClick={() => serviceAction("display-reset", () => seatsApi.resetDisplay(seat.id))}
                    >
                      {actionLoading === "display-reset" ? "..." : "Reset"}
                    </button>
                  }
                />
                <ServiceRow
                  name="Audio"
                  active={services.audio}
                  actions={
                    <button
                      className="btn-sm"
                      disabled={actionLoading !== null}
                      onClick={() => serviceAction("audio-reset", () => seatsApi.resetAudio(seat.id))}
                    >
                      {actionLoading === "audio-reset" ? "..." : "Reset"}
                    </button>
                  }
                />
                <ServiceRow
                  name="Controller"
                  active={services.controller}
                  actions={
                    <button
                      className="btn-sm"
                      disabled={actionLoading !== null}
                      onClick={() => serviceAction("controller-reset", () => seatsApi.resetController(seat.id))}
                    >
                      {actionLoading === "controller-reset" ? "..." : "Reset"}
                    </button>
                  }
                />
                <ServiceRow
                  name="Session"
                  active={seat.sessionId > 0}
                  detail={seat.sessionId > 0 ? `ID: ${seat.sessionId}` : "no session"}
                  actions={
                    <button
                      className="btn-sm"
                      disabled={actionLoading !== null}
                      onClick={() => serviceAction("session-reconnect", () => seatsApi.sessionReconnect(seat.id))}
                      title="Reconnect mstsc to keep session Active. Required for Moonlight streaming — do not disconnect while streaming."
                    >
                      {actionLoading === "session-reconnect" ? "..." : "Reconnect"}
                    </button>
                  }
                />
                <ServiceRow name="Firewall" active={services.firewall} />
                <ServiceRow name="Input Hooks" active={services.inputHooks} />
              </div>
            )}
          </div>
        )}
      </div>

      <div className="card-actions">
        {isActive && (
          <>
            {showLaunch ? (
              <div style={{ display: "flex", gap: 4, flex: 1 }}>
                <input
                  type="text"
                  placeholder="C:\path\to\game.exe"
                  value={launchPath}
                  onChange={(e) => setLaunchPath(e.target.value)}
                  onKeyDown={(e) => e.key === "Enter" && handleLaunch()}
                  style={{ flex: 1 }}
                  disabled={launching}
                />
                <button
                  onClick={handleLaunch}
                  disabled={launching || !launchPath.trim()}
                >
                  {launching ? "..." : "Go"}
                </button>
                <button
                  className="btn-ghost"
                  onClick={() => setShowLaunch(false)}
                >
                  X
                </button>
              </div>
            ) : (
              <button onClick={() => setShowLaunch(true)}>Launch App</button>
            )}
          </>
        )}

        {seat.status !== "Idle" && seat.status !== "TearingDown" && (
          <button
            className="btn-danger"
            onClick={handleDestroy}
            disabled={destroying}
          >
            {destroying ? "Tearing down..." : "Teardown"}
          </button>
        )}
      </div>
    </div>
  );
}

function ServiceRow({
  name,
  active,
  detail,
  actions,
}: {
  name: string;
  active: boolean;
  detail?: string;
  actions?: React.ReactNode;
}) {
  return (
    <div className="service-row">
      <div className="service-status">
        <span
          className="service-dot"
          style={{ background: active ? "var(--success)" : "var(--text-secondary)" }}
        />
        <span className="service-name">{name}</span>
        {detail && <span className="text-muted" style={{ fontSize: 11 }}>({detail})</span>}
      </div>
      {actions && <div className="service-actions">{actions}</div>}
    </div>
  );
}

function MoonlightAddress({ host, port }: { host: string; port: number }) {
  const [copied, setCopied] = useState(false);
  const address = `${host}:${port}`;

  const handleCopy = async () => {
    await navigator.clipboard.writeText(address);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="moonlight-info">
      <span className="stat-label">Moonlight</span>
      <code style={{ flex: 1 }}>{address}</code>
      <button className="copy-btn" onClick={handleCopy} title="Copy address">
        {copied ? "Copied!" : "Copy"}
      </button>
    </div>
  );
}

function StatItem({ label, value }: { label: string; value: string }) {
  return (
    <div className="stat-item">
      <span className="stat-label">{label}</span>
      <span className="stat-value">{value}</span>
    </div>
  );
}

function formatDuration(since: Date): string {
  const ms = Date.now() - since.getTime();
  const secs = Math.floor(ms / 1000);
  if (secs < 60) return `${secs}s`;
  const mins = Math.floor(secs / 60);
  if (mins < 60) return `${mins}m ${secs % 60}s`;
  const hrs = Math.floor(mins / 60);
  return `${hrs}h ${mins % 60}m`;
}
