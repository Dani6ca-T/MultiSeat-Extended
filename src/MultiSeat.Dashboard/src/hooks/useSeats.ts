import { useCallback, useEffect, useRef, useState } from "react";
import type { SeatInfo } from "../api/types";
import { seats as seatsApi } from "../api/client";

/**
 * Hook that provides real-time seat state via WebSocket + REST fallback.
 *
 * On mount:
 *   1. Fetches current seats via GET /api/seats
 *   2. Opens WebSocket to ws://<host>/ws/seats
 *   3. Merges incoming seat updates into state
 *
 * On unmount:
 *   - Closes WebSocket cleanly
 *
 * Auto-reconnects on disconnect (1s, 2s, 4s, 8s, 16s backoff).
 */
export function useSeats() {
  const [seatMap, setSeatMap] = useState<Map<string, SeatInfo>>(new Map());
  const [connected, setConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const wsRef = useRef<WebSocket | null>(null);
  const retriesRef = useRef(0);
  const mountedRef = useRef(true);

  const seats = Array.from(seatMap.values());

  // Merge a single seat update into the map
  const mergeSeat = useCallback((seat: SeatInfo) => {
    setSeatMap((prev) => {
      const next = new Map(prev);
      next.set(seat.id, seat);
      return next;
    });
  }, []);

  // Fetch all seats via REST
  const refresh = useCallback(async () => {
    try {
      const list = await seatsApi.list();
      const map = new Map<string, SeatInfo>();
      for (const s of list) map.set(s.id, s);
      setSeatMap(map);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to fetch seats");
    }
  }, []);

  // WebSocket connection
  const connectWs = useCallback(() => {
    if (!mountedRef.current) return;

    const proto = window.location.protocol === "https:" ? "wss:" : "ws:";
    const url = `${proto}//${window.location.host}/ws/seats`;

    const ws = new WebSocket(url);
    wsRef.current = ws;

    ws.onopen = () => {
      setConnected(true);
      retriesRef.current = 0;
      // Refresh full state on reconnect
      refresh();
    };

    ws.onmessage = (ev) => {
      try {
        const seat: SeatInfo = JSON.parse(ev.data);
        mergeSeat(seat);
      } catch {
        // ignore malformed messages
      }
    };

    ws.onclose = () => {
      setConnected(false);
      wsRef.current = null;

      if (!mountedRef.current) return;

      // Exponential backoff reconnect
      const delay = Math.min(1000 * 2 ** retriesRef.current, 16000);
      retriesRef.current++;
      setTimeout(connectWs, delay);
    };

    ws.onerror = () => {
      ws.close();
    };
  }, [mergeSeat, refresh]);

  useEffect(() => {
    mountedRef.current = true;
    refresh();
    connectWs();

    return () => {
      mountedRef.current = false;
      wsRef.current?.close();
    };
  }, [connectWs, refresh]);

  return { seats, connected, error, refresh };
}
