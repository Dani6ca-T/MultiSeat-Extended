import { useCallback } from "react";
import { system, input } from "../api/client";
import { usePolling } from "../hooks/usePolling";
import type { HookStatus } from "../api/types";

export function SystemPage() {
  const healthFetcher = useCallback(() => system.health(), []);
  const hookFetcher = useCallback(() => input.hookStatus().catch(() => null), []);

  const { data: health, loading, error } = usePolling(healthFetcher, 5000);
  const { data: hookStatus } = usePolling<HookStatus | null>(hookFetcher, 5000);

  if (loading) return <div className="text-muted">Loading system status...</div>;
  if (error) return <div className="error-banner">{error}</div>;
  if (!health) return null;

  const memUsedMb = health.systemMemoryMb - health.availableMemoryMb;
  const memPercent =
    health.systemMemoryMb > 0
      ? Math.round((memUsedMb / health.systemMemoryMb) * 100)
      : 0;

  return (
    <div>
      <div className="page-header">
        <h2>System Health</h2>
        <span className="text-muted">
          Updated {new Date(health.timestamp).toLocaleTimeString()}
        </span>
      </div>

      <div className="card-grid">
        {/* Seats */}
        <div className="card">
          <div className="card-body">
            <h3>Seats</h3>
            <div className="big-number">{health.activeSeats}</div>
            <div className="text-muted">of {health.maxSeats} max</div>
            <ProgressBar
              value={health.activeSeats}
              max={health.maxSeats}
              color="#3b82f6"
            />
          </div>
        </div>

        {/* Memory */}
        <div className="card">
          <div className="card-body">
            <h3>Memory</h3>
            <div className="big-number">{memPercent}%</div>
            <div className="text-muted">
              {formatMb(memUsedMb)} / {formatMb(health.systemMemoryMb)}
            </div>
            <ProgressBar value={memPercent} max={100} color="#f59e0b" />
          </div>
        </div>

        {/* GPU */}
        {health.gpu && (
          <div className="card">
            <div className="card-body">
              <h3>GPU</h3>
              <div style={{ fontSize: 14, fontWeight: 600 }}>
                {health.gpu.name}
              </div>
              <div className="stat-grid" style={{ marginTop: 12 }}>
                <div className="stat-item">
                  <span className="stat-label">Utilization</span>
                  <span className="stat-value">
                    {health.gpu.utilizationPercent}%
                  </span>
                </div>
                <div className="stat-item">
                  <span className="stat-label">Encoder</span>
                  <span className="stat-value">
                    {health.gpu.encoderUtilizationPercent}%
                  </span>
                </div>
                <div className="stat-item">
                  <span className="stat-label">VRAM</span>
                  <span className="stat-value">
                    {formatMb(health.gpu.vramUsedMb)} /{" "}
                    {formatMb(health.gpu.vramTotalMb)}
                  </span>
                </div>
                <div className="stat-item">
                  <span className="stat-label">Encoder Sessions</span>
                  <span className="stat-value">
                    {health.gpu.activeEncoderSessions}
                  </span>
                </div>
              </div>
              <ProgressBar
                value={health.gpu.utilizationPercent}
                max={100}
                color="#10b981"
              />
            </div>
          </div>
        )}

        {/* System Info */}
        <div className="card">
          <div className="card-body">
            <h3>System</h3>
            <div className="stat-grid">
              <div className="stat-item">
                <span className="stat-label">Windows</span>
                <span className="stat-value">{health.windowsBuild}</span>
              </div>
              <div className="stat-item">
                <span className="stat-label">RDP Wrapper</span>
                <span className="stat-value">
                  {health.rdpWrapperActive ? (
                    <span style={{ color: "var(--success)" }}>Active</span>
                  ) : (
                    <span style={{ color: "var(--danger)" }}>Inactive</span>
                  )}
                </span>
              </div>
              <div className="stat-item">
                <span className="stat-label">Input Hooks</span>
                <span className="stat-value">
                  {hookStatus?.installed ? (
                    <span style={{ color: "var(--success)" }}>Active</span>
                  ) : (
                    <span style={{ color: "var(--text-secondary)" }}>Idle</span>
                  )}
                </span>
              </div>
              <div className="stat-item">
                <span className="stat-label">API Port</span>
                <span className="stat-value">
                  {window.location.port || "80"}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function ProgressBar({
  value,
  max,
  color,
}: {
  value: number;
  max: number;
  color: string;
}) {
  const pct = max > 0 ? Math.min(100, (value / max) * 100) : 0;
  return (
    <div className="progress-bar">
      <div
        className="progress-fill"
        style={{ width: `${pct}%`, backgroundColor: color }}
      />
    </div>
  );
}

function formatMb(mb: number): string {
  if (mb >= 1024) return `${(mb / 1024).toFixed(1)} GB`;
  return `${mb} MB`;
}
