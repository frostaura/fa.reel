import { useEffect } from "react";
import { useDispatch } from "react-redux";
import { api } from "../store/api";
import { syncIdle, syncProgress } from "../store/syncSlice";
import type { AppDispatch } from "../store";

/**
 * Headless steady-state SSE consumer mounted once in the app shell (fa.foresight's
 * realtimeSync pattern). Background jobs (delta syncs, retrains, feed rebuilds) report via
 * `job-progress` / `job-completed`; completion invalidates RTK tags so any mounted query
 * refetches — strictly cheaper than polling. `onerror` is a no-op: EventSource auto-reconnects
 * and refetch-on-focus remains the authoritative fallback.
 */
export default function RealtimeSync() {
  const dispatch = useDispatch<AppDispatch>();

  useEffect(() => {
    const source = new EventSource("/sse/pipeline");

    source.addEventListener("job-progress", (ev) => {
      try {
        const data = JSON.parse((ev as MessageEvent).data);
        dispatch(
          syncProgress({
            label: String(data.message ?? data.kind ?? "Syncing"),
            pct: typeof data.pct === "number" ? data.pct : null,
          })
        );
      } catch {
        // ignore malformed frame
      }
    });

    source.addEventListener("job-completed", () => {
      dispatch(syncIdle());
      dispatch(api.util.invalidateTags(["Sync", "Feed", "TasteDna"]));
    });

    return () => source.close();
  }, [dispatch]);

  return null;
}
