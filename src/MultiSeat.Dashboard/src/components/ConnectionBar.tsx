interface Props {
  connected: boolean;
  onReconnect: () => void;
}

export function ConnectionBar({ connected, onReconnect }: Props) {
  if (connected) return null;

  return (
    <div className="connection-bar">
      <span className="connection-dot" />
      <span>WebSocket disconnected — updates paused</span>
      <button className="btn-sm btn-ghost" onClick={onReconnect}>
        Retry
      </button>
    </div>
  );
}
