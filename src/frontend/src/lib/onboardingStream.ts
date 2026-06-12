import { useEffect } from "react";
import { useDispatch } from "react-redux";
import { eventReceived, markStalled } from "../store/onboardingSlice";
import type { AppDispatch } from "../store";

/** Named SSE events the pipeline hub emits. Keep in sync with the backend PipelineEventHub. */
const EVENT_TYPES = [
  "connected",
  "stage-changed",
  "ingest-progress",
  "insight",
  "model-progress",
  "feed-ready",
  "failed",
  "heartbeat",
] as const;

const STALL_AFTER_MS = 20_000;

/**
 * Opens the per-account pipeline SSE stream and pumps named events into onboardingSlice.
 *
 * Mirrors fa.foresight's trainingStream contract: `onerror` is a no-op — EventSource
 * auto-reconnects, replays are made idempotent by seq-dedupe in the reducer, and the
 * caller's sync/status poll remains the authoritative fallback. A 20s client-side stall
 * timer (reset on any event, heartbeats included) flips UI copy to "still working" mode.
 */
export function useOnboardingStream(enabled: boolean): void {
  const dispatch = useDispatch<AppDispatch>();

  useEffect(() => {
    if (!enabled) return;

    const source = new EventSource("/sse/pipeline");
    let stallTimer: number | undefined;

    const resetStallTimer = () => {
      window.clearTimeout(stallTimer);
      stallTimer = window.setTimeout(() => dispatch(markStalled()), STALL_AFTER_MS);
    };

    for (const type of EVENT_TYPES) {
      source.addEventListener(type, (ev) => {
        resetStallTimer();
        try {
          const data = JSON.parse((ev as MessageEvent).data);
          dispatch(eventReceived({ type, data }));
        } catch {
          // Malformed frame — ignore; the next event or the status poll recovers state.
        }
      });
    }

    resetStallTimer();

    return () => {
      window.clearTimeout(stallTimer);
      source.close();
    };
  }, [enabled, dispatch]);
}
