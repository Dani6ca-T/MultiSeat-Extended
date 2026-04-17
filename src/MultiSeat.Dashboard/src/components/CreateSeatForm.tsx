import { useState } from "react";
import type { AccountInfo } from "../api/types";
import { seats as seatsApi } from "../api/client";

interface Props {
  accounts: AccountInfo[];
  onCreated: () => void;
}

const RESOLUTIONS = [
  { label: "720p", w: 1280, h: 720 },
  { label: "1080p", w: 1920, h: 1080 },
  { label: "1440p", w: 2560, h: 1440 },
  { label: "4K", w: 3840, h: 2160 },
  { label: "Ally X", w: 1920, h: 1200 },
  { label: "Deck", w: 1280, h: 800 },
];

export function CreateSeatForm({ accounts, onCreated }: Props) {
  const [accountName, setAccountName] = useState("");
  const [resolution, setResolution] = useState("1080p");
  const [fps, setFps] = useState(60);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!accountName) return;

    const res = RESOLUTIONS.find((r) => r.label === resolution) ?? RESOLUTIONS[1];

    setCreating(true);
    setError(null);
    try {
      await seatsApi.create({
        accountName,
        width: res.w,
        height: res.h,
        fps,
      });
      onCreated();
      setAccountName("");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to create seat");
    } finally {
      setCreating(false);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="create-form">
      <h3>New Seat</h3>

      <label>
        Account
        <select value={accountName} onChange={(e) => setAccountName(e.target.value)}>
          <option value="">Select account...</option>
          {accounts.map((a) => (
            <option key={a.username} value={a.username}>
              {a.username}
            </option>
          ))}
        </select>
      </label>

      <label>
        Resolution
        <select value={resolution} onChange={(e) => setResolution(e.target.value)}>
          {RESOLUTIONS.map((r) => (
            <option key={r.label} value={r.label}>
              {r.label} ({r.w}x{r.h})
            </option>
          ))}
        </select>
      </label>

      <label>
        FPS
        <select value={fps} onChange={(e) => setFps(Number(e.target.value))}>
          <option value={30}>30</option>
          <option value={60}>60</option>
          <option value={90}>90</option>
          <option value={120}>120</option>
          <option value={144}>144</option>
          <option value={160}>160</option>
          <option value={240}>240</option>
        </select>
      </label>

      {error && <div className="error-banner">{error}</div>}

      <button type="submit" disabled={creating || !accountName}>
        {creating ? "Provisioning..." : "Provision Seat"}
      </button>
    </form>
  );
}
