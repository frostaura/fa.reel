import { useEffect, useReducer } from "react";
import { streamAskReel, type AskCard, type PhaseData } from "./askReelStream";

type Status = "idle" | "streaming" | "done" | "error";

interface State {
  status: Status;
  phase: PhaseData | null;
  byId: Record<string, AskCard>;
  order: string[];
  reason: string | null;
}

type Action =
  | { type: "reset" }
  | { type: "phase"; data: PhaseData }
  | { type: "candidate"; data: AskCard }
  | { type: "scored"; data: { titleId: string; predictedRating: number | null } }
  | { type: "reranked"; data: { titleId: string; fit: number | null; why: string | null } }
  | { type: "done"; data: { results: AskCard[]; reason: string | null } }
  | { type: "error" };

const initial: State = { status: "idle", phase: null, byId: {}, order: [], reason: null };

function reducer(state: State, action: Action): State {
  switch (action.type) {
    case "reset":
      return { status: "streaming", phase: null, byId: {}, order: [], reason: null };
    case "phase":
      return { ...state, status: "streaming", phase: action.data };
    case "candidate": {
      const c = action.data;
      if (state.byId[c.titleId]) return state;
      return { ...state, byId: { ...state.byId, [c.titleId]: c }, order: [...state.order, c.titleId] };
    }
    case "scored": {
      const existing = state.byId[action.data.titleId];
      if (!existing) return state;
      return {
        ...state,
        byId: { ...state.byId, [action.data.titleId]: { ...existing, predictedRating: action.data.predictedRating } },
      };
    }
    case "reranked": {
      const existing = state.byId[action.data.titleId];
      if (!existing) return state;
      return {
        ...state,
        byId: { ...state.byId, [action.data.titleId]: { ...existing, fit: action.data.fit, why: action.data.why } },
      };
    }
    case "done": {
      const byId = { ...state.byId };
      for (const c of action.data.results) byId[c.titleId] = { ...byId[c.titleId], ...c };
      return {
        ...state,
        byId,
        order: action.data.results.map((c) => c.titleId),
        reason: action.data.reason,
        status: "done",
      };
    }
    case "error":
      return { ...state, status: "error" };
  }
}

/**
 * Drives the live Ask Reel stream for a query: opens the POST-SSE stream, accumulates cards as
 * they arrive (relevance order), merges in personal scores + LLM rerank as they land, and snaps
 * to the final blended order on done. Aborts the in-flight stream when the query changes.
 */
export function useAskReel(query: string) {
  const [state, dispatch] = useReducer(reducer, initial);

  useEffect(() => {
    if (!query.trim()) return;
    const controller = new AbortController();
    dispatch({ type: "reset" });

    streamAskReel({ query }, (ev) => {
      switch (ev.event) {
        case "phase":
          dispatch({ type: "phase", data: ev.data });
          break;
        case "candidate":
          dispatch({ type: "candidate", data: ev.data });
          break;
        case "candidate-scored":
          dispatch({ type: "scored", data: ev.data });
          break;
        case "candidate-reranked":
          dispatch({ type: "reranked", data: ev.data });
          break;
        case "done":
          dispatch({ type: "done", data: ev.data });
          break;
        default:
          break;
      }
    }, controller.signal).catch((err) => {
      if (!controller.signal.aborted) dispatch({ type: "error" });
      void err;
    });

    return () => controller.abort();
  }, [query]);

  const cards = state.order.map((id) => state.byId[id]).filter(Boolean);
  return { status: state.status, phase: state.phase, cards, reason: state.reason };
}
