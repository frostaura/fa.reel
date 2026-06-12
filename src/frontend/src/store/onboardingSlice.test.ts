import { describe, expect, it } from "vitest";
import reducer, {
  eventReceived,
  markStalled,
  pollAdvance,
  reset,
  stageFromPipeline,
  type OnboardingState,
} from "./onboardingSlice";

const initial = (): OnboardingState => reducer(undefined, { type: "@@init" });

const ev = (type: string, data: Record<string, unknown>) => eventReceived({ type, data: { seq: 0, ...data } });

describe("onboardingSlice", () => {
  it("dedupes replayed events by seq", () => {
    let s = initial();
    s = reducer(s, ev("ingest-progress", { seq: 5, kind: "movies", found: 100 }));
    expect(s.counters.movies).toBe(100);
    // Replay of an older frame after EventSource reconnect must be dropped.
    s = reducer(s, ev("ingest-progress", { seq: 3, kind: "movies", found: 50 }));
    expect(s.counters.movies).toBe(100);
    expect(s.lastSeq).toBe(5);
  });

  it("treats seq 0 as unsequenced and always applies it", () => {
    let s = initial();
    s = reducer(s, ev("ingest-progress", { seq: 10, kind: "shows", found: 20 }));
    s = reducer(s, ev("heartbeat", { seq: 0 }));
    expect(s.stalled).toBe(false);
  });

  it("advances stage monotonically and never regresses", () => {
    let s = initial();
    s = reducer(s, ev("insight", { seq: 1, id: "a", kind: "genre", text: "You love sci-fi" }));
    expect(s.stage).toBe("profiling");
    // A late ingest-progress (lower stage) must not pull the show backwards.
    s = reducer(s, ev("ingest-progress", { seq: 2, kind: "ratings", found: 3000 }));
    expect(s.stage).toBe("profiling");
    expect(s.counters.ratings).toBe(3000);
  });

  it("failed always applies regardless of current stage", () => {
    let s = initial();
    s = reducer(s, ev("feed-ready", { seq: 1 }));
    expect(s.stage).toBe("reveal");
    s = reducer(s, ev("failed", { seq: 2, error: "token revoked" }));
    expect(s.stage).toBe("failed");
    expect(s.error).toBe("token revoked");
  });

  it("counters only count up (out-of-order progress frames)", () => {
    let s = initial();
    s = reducer(s, ev("ingest-progress", { seq: 0, kind: "episodes", found: 500 }));
    s = reducer(s, ev("ingest-progress", { seq: 0, kind: "episodes", found: 400 }));
    expect(s.counters.episodes).toBe(500);
  });

  it("dedupes insights by id and caps the list", () => {
    let s = initial();
    s = reducer(s, ev("insight", { seq: 0, id: "x", kind: "creator", text: "Villeneuve 9.1" }));
    s = reducer(s, ev("insight", { seq: 0, id: "x", kind: "creator", text: "Villeneuve 9.1" }));
    expect(s.insights).toHaveLength(1);
  });

  it("poll data advances a stalled show (poll is authoritative)", () => {
    let s = initial();
    s = reducer(s, ev("ingest-progress", { seq: 1, kind: "movies", found: 10 }));
    s = reducer(s, markStalled());
    expect(s.stalled).toBe(true);
    s = reducer(s, pollAdvance({ pipelineStage: "Training" }));
    expect(s.stage).toBe("fitting");
  });

  it("maps backend pipeline stages to show stages", () => {
    expect(stageFromPipeline("Ingesting")).toBe("ingesting");
    expect(stageFromPipeline("Extracting")).toBe("profiling");
    expect(stageFromPipeline("Training")).toBe("fitting");
    expect(stageFromPipeline("Evaluated")).toBe("fitting");
    expect(stageFromPipeline("FeedReady")).toBe("reveal");
    expect(stageFromPipeline("Degraded")).toBe("failed");
    expect(stageFromPipeline("Linked")).toBeNull();
  });

  it("reset returns to the initial state", () => {
    let s = initial();
    s = reducer(s, ev("feed-ready", { seq: 9 }));
    s = reducer(s, reset());
    expect(s.stage).toBe("connecting");
    expect(s.lastSeq).toBe(0);
  });
});
