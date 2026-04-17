import { useCallback, useEffect, useRef, useState } from "react";

/**
 * Generic polling hook. Calls `fetcher` every `intervalMs` milliseconds.
 * Stops polling when the component unmounts.
 */
export function usePolling<T>(
  fetcher: () => Promise<T>,
  intervalMs: number = 5000
) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const timerRef = useRef<ReturnType<typeof setInterval>>(undefined);

  const fetch = useCallback(async () => {
    try {
      const result = await fetcher();
      setData(result);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Fetch failed");
    } finally {
      setLoading(false);
    }
  }, [fetcher]);

  useEffect(() => {
    fetch();
    timerRef.current = setInterval(fetch, intervalMs);
    return () => clearInterval(timerRef.current);
  }, [fetch, intervalMs]);

  return { data, error, loading, refresh: fetch };
}
