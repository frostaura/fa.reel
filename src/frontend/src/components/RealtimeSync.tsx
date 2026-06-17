import { useEffect, useRef } from "react";
import { useDispatch } from "react-redux";
import { api, useGetSyncStatusQuery } from "../store/api";
import { syncIdle, syncProgress } from "../store/syncSlice";
import { jobKindToSseKind, isBackgroundKind } from "../lib/syncCopy";
import type { AppDispatch } from "../store";

/**
 * Headless steady-state SSE consumer mounted once in the app shell (fa.foresight's
 * realtimeSync pattern). Two inputs keep the header pill honest:
 *   1. Seed — /api/sync/status (polled every 5s, and on tag invalidation), so a reload
 *      mid-job shows the pill immediately and it self-corrects to the authoritative
 *      active job — the endpoint prefers a primary pipeline job over a background poll.
 *   2. Live — `job-progress` events update it in place between polls; `job-completed`
 *      invalidates the Sync tag so the refetch confirms idle (or the next active job).
 *
 * A background Trakt poll (delta/reconcile) fires every few minutes and must NOT hijack the
 * pill from a primary job (onboarding, enrichment, training) the user is waiting on — so its
 * live events are ignored while a primary job is active. `onerror` is a no-op: EventSource
 * auto-reconnects and the poll is the fallback.
 */
export default function RealtimeSync() {
  const dispatch = useDispatch<AppDispatch>();
  const { data: status } = useGetSyncStatusQuery(undefined, { pollingInterval: 5000 });

  // Tracks whether the authoritative active job is a primary pipeline job, so the SSE handler
  // can drop background-poll events that would otherwise clobber its progress.
  const primaryActiveRef = useRef(false);

  useEffect(() => {
    if (!status) return;
    const job = status.activeJob;
    if (job && (job.status === "Running" || job.status === "Pending")) {
      const kind = jobKindToSseKind(job.kind);
      primaryActiveRef.current = !isBackgroundKind(kind);
      dispatch(syncProgress({ label: job.progressMessage ?? "Syncing", pct: job.progressPct, kind }));
    } else {
      primaryActiveRef.current = false;
      dispatch(syncIdle());
    }
  }, [status, dispatch]);

  useEffect(() => {
    const source = new EventSource("/sse/pipeline");

    source.addEventListener("job-progress", (ev) => {
      try {
        const data = JSON.parse((ev as MessageEvent).data);
        const kind = typeof data.kind === "string" ? data.kind : null;
        // A background poll must not overwrite a primary job's live progress.
        if (isBackgroundKind(kind) && primaryActiveRef.current) return;
        if (!isBackgroundKind(kind)) primaryActiveRef.current = true;
        dispatch(
          syncProgress({
            label: String(data.message ?? data.kind ?? "Syncing"),
            pct: typeof data.pct === "number" ? data.pct : null,
            kind,
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
