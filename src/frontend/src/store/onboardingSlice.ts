import { createSlice, type PayloadAction } from "@reduxjs/toolkit";

/**
 * The onboarding live build-up state machine.
 *
 * Fed by `/sse/pipeline` named events (see lib/onboardingStream.ts). Two hard rules keep the
 * show robust against SSE replays and out-of-order delivery:
 *   1. seq-dedupe — every event carries a monotonic per-account `seq`; stale ones are dropped.
 *   2. stage monotonicity — the show only advances (except `failed`, which always applies).
 * The 15s sync/status poll is authoritative: pollAdvance() can move the stage even when SSE
 * stalls, so the show never wedges on a dropped connection.
 */
export type ShowStage = "connecting" | "ingesting" | "profiling" | "fitting" | "reveal" | "failed";

const STAGE_ORDER: Record<ShowStage, number> = {
  connecting: 0,
  ingesting: 1,
  profiling: 2,
  fitting: 3,
  reveal: 4,
  failed: 99,
};

export type IngestKind = "movies" | "shows" | "episodes" | "ratings";

export interface Insight {
  id: string;
  kind: "library" | "genre" | "era" | "creator" | "tone" | "stat";
  text: string;
  payload?: Record<string, unknown>;
}

export interface ModelProgress {
  phase: string;
  processed: number;
  total: number;
}

export interface PipelineEvent {
  type: string;
  data: {
    seq: number;
    [key: string]: unknown;
  };
}

export interface OnboardingState {
  stage: ShowStage;
  counters: Partial<Record<IngestKind, number>>;
  insights: Insight[];
  modelProgress: ModelProgress | null;
  traktUser: string | null;
  avatarUrl: string | null;
  lastSeq: number;
  lastEventAt: number | null;
  stalled: boolean;
  error: string | null;
}

const initialState: OnboardingState = {
  stage: "connecting",
  counters: {},
  insights: [],
  modelProgress: null,
  traktUser: null,
  avatarUrl: null,
  lastSeq: 0,
  lastEventAt: null,
  stalled: false,
  error: null,
};

const MAX_INSIGHTS = 50;

function advance(state: OnboardingState, target: ShowStage) {
  if (target === "failed" || STAGE_ORDER[target] > STAGE_ORDER[state.stage]) {
    state.stage = target;
  }
}

/** Backend PipelineStage → show stage, used by both stage-changed events and the status poll. */
export function stageFromPipeline(pipelineStage: string): ShowStage | null {
  switch (pipelineStage) {
    case "Ingesting": return "ingesting";
    case "Extracting": return "profiling";
    case "Training": return "fitting";
    case "Evaluated": return "fitting";
    case "FeedReady": return "reveal";
    case "Degraded": return "failed";
    default: return null;
  }
}

export const onboardingSlice = createSlice({
  name: "onboarding",
  initialState,
  reducers: {
    eventReceived(state, action: PayloadAction<PipelineEvent>) {
      const { type, data } = action.payload;
      const seq = typeof data.seq === "number" ? data.seq : 0;
      if (seq !== 0 && seq <= state.lastSeq) return; // replayed event after reconnect — drop
      if (seq !== 0) state.lastSeq = seq;
      state.lastEventAt = Date.now();
      state.stalled = false;

      switch (type) {
        case "connected":
          state.traktUser = (data.traktUser as string) ?? state.traktUser;
          state.avatarUrl = (data.avatarUrl as string) ?? state.avatarUrl;
          break;
        case "ingest-progress": {
          const kind = data.kind as IngestKind;
          const found = data.found as number;
          if (kind != null && typeof found === "number") {
            state.counters[kind] = Math.max(state.counters[kind] ?? 0, found);
          }
          advance(state, "ingesting");
          break;
        }
        case "insight": {
          advance(state, "profiling");
          const insight: Insight = {
            id: String(data.id ?? seq),
            kind: (data.kind as Insight["kind"]) ?? "stat",
            text: String(data.text ?? ""),
            payload: data.payload as Record<string, unknown> | undefined,
          };
          if (!state.insights.some((i) => i.id === insight.id)) {
            state.insights.push(insight);
            if (state.insights.length > MAX_INSIGHTS) state.insights.shift();
          }
          break;
        }
        case "model-progress":
          advance(state, "fitting");
          state.modelProgress = {
            phase: String(data.phase ?? ""),
            processed: (data.processed as number) ?? 0,
            total: (data.total as number) ?? 0,
          };
          break;
        case "stage-changed": {
          const mapped = stageFromPipeline(String(data.stage ?? ""));
          if (mapped) advance(state, mapped);
          break;
        }
        case "feed-ready":
          advance(state, "reveal");
          break;
        case "failed":
          state.error = String(data.error ?? "Something went wrong during sync.");
          advance(state, "failed");
          break;
        case "heartbeat":
        default:
          break; // heartbeats only refresh lastEventAt/stalled above
      }
    },
    /** The authoritative sync/status poll can advance the show when SSE is stalled or lost. */
    pollAdvance(state, action: PayloadAction<{ pipelineStage: string }>) {
      const mapped = stageFromPipeline(action.payload.pipelineStage);
      if (mapped) advance(state, mapped);
    },
    markStalled(state) {
      state.stalled = true;
    },
    reset() {
      return initialState;
    },
  },
});

export const { eventReceived, pollAdvance, markStalled, reset } = onboardingSlice.actions;
export default onboardingSlice.reducer;
