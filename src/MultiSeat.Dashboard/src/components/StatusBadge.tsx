import type { SeatStatus } from "../api/types";

const STATUS_COLORS: Record<SeatStatus, string> = {
  Idle: "#6b7280",
  Provisioning: "#f59e0b",
  Configuring: "#f59e0b",
  Connecting: "#f59e0b",
  Ready: "#10b981",
  Streaming: "#3b82f6",
  TearingDown: "#ef4444",
  Error: "#ef4444",
};

const STATUS_LABELS: Record<SeatStatus, string> = {
  Idle: "Idle",
  Provisioning: "Provisioning...",
  Configuring: "Configuring...",
  Connecting: "Reconnecting...",
  Ready: "Ready",
  Streaming: "Streaming",
  TearingDown: "Tearing Down...",
  Error: "Error",
};

export function StatusBadge({ status }: { status: SeatStatus }) {
  const color = STATUS_COLORS[status] ?? "#6b7280";
  const label = STATUS_LABELS[status] ?? status;
  const isAnimated = status === "Provisioning" || status === "Configuring" ||
    status === "Connecting" || status === "TearingDown";

  return (
    <span
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 6,
        padding: "2px 10px",
        borderRadius: 12,
        fontSize: 13,
        fontWeight: 600,
        color: "#fff",
        backgroundColor: color,
      }}
    >
      {isAnimated && (
        <span
          style={{
            width: 8,
            height: 8,
            borderRadius: "50%",
            backgroundColor: "#fff",
            animation: "pulse 1s infinite",
          }}
        />
      )}
      {label}
    </span>
  );
}
