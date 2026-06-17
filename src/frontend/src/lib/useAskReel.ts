import { useEffect, useReducer } from "react";
import { streamAskReel, type AskCard, type PhaseData } from "./askReelStream";

type Status = "idle" | "streaming" | "done" | "error";

interface State {
  status: Status;
  phase: PhaseData | null;
  assistant: string | null;
  byId: Record<string, AskCard>;
  order: string[];
  reason: string | null;
}

type Action =
  | { type: "reset" }
  | { type: "phase"; data: PhaseData }
  | { type: "assistant"; data: { text: string } }
  | { type: "candidate"; data: AskCard }
  | { type: "scored"; data: { titleId: string; predictedRating: number | null } }
  | { type: "reranked"; data: { titleId: string; fit: number | null; why: string | null } }
  | { type: "done"; data: { results: AskCard[]; reason: string | null } }
  | { type: "error" };

const initial: State = { status: "idle", phase: null, assistant: null, byId: {}, order: [], reason: null };

function reducer(state: State, action: Action): State {
  switch (action.type) {
    case "reset":
      return { status: "streaming", phase: null, assistant: null, byId: {}, order: [], reason: null };
    case "phase":
      return { ...state, status: "streaming", phase: action.data };
    case "assistant":
      return { ...state, assistant: action.data.text };
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
        case "assistant-message":
          dispatch({ type: "assistant", data: ev.data });
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

  // Sort client-side by the same blend the server uses — so tiles visibly re-order live as
  // personal scores and hyper-personal LLM fit stream in (insertion order breaks ties; sort is stable).
  const cards = state.order
    .map((id) => state.byId[id])
    .filter(Boolean)
    .sort((a, b) => blend(b) - blend(a));
  return { status: state.status, phase: state.phase, assistant: state.assistant, cards, reason: state.reason };
}

function blend(c: AskCard): number {
  const sim = c.similarity ?? 0;
  const predicted = (c.predictedRating ?? 6) / 10;
  const fit = c.fit ?? 0.5;
  return sim * 0.35 + predicted * 0.25 + fit * 0.4;
}
