import { useEffect } from "react";
import { useDispatch } from "react-redux";
import { api, useGetSyncStatusQuery } from "../store/api";
import { syncIdle, syncProgress } from "../store/syncSlice";
import { jobKindToSseKind } from "../lib/syncCopy";
import type { AppDispatch } from "../store";

/**
 * Headless steady-state SSE consumer mounted once in the app shell (fa.foresight's
 * realtimeSync pattern). Two inputs keep the header pill honest:
 *   1. Seed — /api/sync/status on mount (and on tag invalidation), so a page reload
 *      mid-job shows the pill immediately instead of waiting for the next SSE batch.
 *   2. Live — `job-progress` events update it in place; `job-completed` invalidates the
 *      Sync tag, the status refetch then confirms idle (or the next active job).
 * `onerror` is a no-op: EventSource auto-reconnects and refetch-on-focus is the fallback.
 */
export default function RealtimeSync() {
  const dispatch = useDispatch<AppDispatch>();
  const { data: status } = useGetSyncStatusQuery();

  useEffect(() => {
    if (!status) return;
    const job = status.activeJob;
    if (job && (job.status === "Running" || job.status === "Pending")) {
      dispatch(
        syncProgress({
          label: job.progressMessage ?? "Syncing",
          pct: job.progressPct,
          kind: jobKindToSseKind(job.kind),
        })
      );
    } else {
      dispatch(syncIdle());
    }
  }, [status, dispatch]);

  useEffect(() => {
    const source = new EventSource("/sse/pipeline");

    source.addEventListener("job-progress", (ev) => {
      try {
        const data = JSON.parse((ev as MessageEvent).data);
        dispatch(
          syncProgress({
            label: String(data.message ?? data.kind ?? "Syncing"),
            pct: typeof data.pct === "number" ? data.pct : null,
            kind: typeof data.kind === "string" ? data.kind : null,
          })
        );
      } catch {
        // ignore malformed frame
      }
    });

    source.addEventListener("job-completed", () => {
      // The status refetch (Sync tag) decides whether the pill clears or hands over to
      // the next active job.
      dispatch(api.util.invalidateTags(["Sync", "Feed", "TasteDna"]));
    });

    return () => source.close();
  }, [dispatch]);

  return null;
}
