import { useCallback, useEffect, useReducer, useRef } from "react";
import { streamAskReel, type AskCard, type PhaseData } from "./askReelStream";

type Status = "idle" | "streaming" | "done" | "error";

export interface ChatTurn {
  role: "user" | "assistant";
  text: string;
}

interface State {
  turns: ChatTurn[];
  status: Status;
  phase: PhaseData | null;
  byId: Record<string, AskCard>;
  order: string[];
  reason: string | null;
  shown: number[]; // tmdbIds surfaced across the whole conversation (no-repeat)
  conversationId: string | null;
}

type Action =
  | { type: "userTurn"; message: string; fresh: boolean }
  | { type: "phase"; data: PhaseData }
  | { type: "conversation"; data: { id: string } }
  | { type: "resume"; turns: ChatTurn[]; conversationId: string }
  | { type: "assistant"; data: { text: string } }
  | { type: "candidate"; data: AskCard }
  | { type: "scored"; data: { titleId: string; predictedRating: number | null } }
  | { type: "reranked"; data: { titleId: string; fit: number | null; why: string | null } }
  | { type: "done"; data: { results: AskCard[]; reason: string | null } }
  | { type: "error" };

const initial: State = { turns: [], status: "idle", phase: null, byId: {}, order: [], reason: null, shown: [], conversationId: null };

function reducer(state: State, action: Action): State {
  switch (action.type) {
    case "userTurn": {
      const turn: ChatTurn = { role: "user", text: action.message };
      return action.fresh
        ? { ...initial, turns: [turn], status: "streaming" }
        : { ...state, turns: [...state.turns, turn], status: "streaming", phase: null, byId: {}, order: [], reason: null };
    }
    case "phase":
      return { ...state, status: "streaming", phase: action.data };
    case "conversation":
      return { ...state, conversationId: action.data.id };
    case "resume":
      return { ...initial, turns: action.turns, conversationId: action.conversationId, status: "done" };
    case "assistant":
      return { ...state, turns: [...state.turns, { role: "assistant", text: action.data.text }] };
    case "candidate": {
      const c = action.data;
      if (state.byId[c.titleId]) return state;
      return { ...state, byId: { ...state.byId, [c.titleId]: c }, order: [...state.order, c.titleId] };
    }
    case "scored": {
      const e = state.byId[action.data.titleId];
      if (!e) return state;
      return { ...state, byId: { ...state.byId, [action.data.titleId]: { ...e, predictedRating: action.data.predictedRating } } };
    }
    case "reranked": {
      const e = state.byId[action.data.titleId];
      if (!e) return state;
      return { ...state, byId: { ...state.byId, [action.data.titleId]: { ...e, fit: action.data.fit, why: action.data.why } } };
    }
    case "done": {
      const byId = { ...state.byId };
      const newTmdb: number[] = [];
      for (const c of action.data.results) {
        byId[c.titleId] = { ...byId[c.titleId], ...c };
        if (c.tmdbId != null) newTmdb.push(c.tmdbId);
      }
      const shown = Array.from(new Set([...state.shown, ...newTmdb]));
      return { ...state, byId, order: action.data.results.map((c) => c.titleId), reason: action.data.reason, status: "done", shown };
    }
    case "error":
      return { ...state, status: "error" };
  }
}

function blend(c: AskCard): number {
  return (c.similarity ?? 0) * 0.35 + ((c.predictedRating ?? 6) / 10) * 0.25 + (c.fit ?? 0.5) * 0.4;
}

/**
 * Drives a multi-turn Ask Reel conversation: each turn streams from /api/search/ask with the
 * prior transcript + the tmdbIds already shown (so nothing repeats), accumulates cards (re-sorted
 * live by the personal blend), and records the reply. `fresh` starts a new conversation.
 */
export function useAskReelChat() {
  const [state, dispatch] = useReducer(reducer, initial);
  const stateRef = useRef(state);
  stateRef.current = state;
  const abortRef = useRef<AbortController | null>(null);

  const send = useCallback((message: string, fresh = false) => {
    const msg = message.trim();
    if (!msg) return;
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    const history = fresh ? [] : stateRef.current.turns.map((t) => ({ role: t.role, text: t.text }));
    const shownTmdbIds = fresh ? [] : [...stateRef.current.shown];
    const conversationId = fresh ? null : stateRef.current.conversationId;
    dispatch({ type: "userTurn", message: msg, fresh });

    streamAskReel({ query: msg, history, shownTmdbIds, conversationId }, (ev) => {
      switch (ev.event) {
        case "phase": dispatch({ type: "phase", data: ev.data }); break;
        case "conversation": dispatch({ type: "conversation", data: ev.data }); break;
        case "assistant-message": dispatch({ type: "assistant", data: ev.data }); break;
        case "candidate": dispatch({ type: "candidate", data: ev.data }); break;
        case "candidate-scored": dispatch({ type: "scored", data: ev.data }); break;
        case "candidate-reranked": dispatch({ type: "reranked", data: ev.data }); break;
        case "done": dispatch({ type: "done", data: ev.data }); break;
        default: break;
      }
    }, controller.signal).catch((err) => {
      if (!controller.signal.aborted) dispatch({ type: "error" });
      void err;
    });
  }, []);

  const resume = useCallback((turns: ChatTurn[], conversationId: string) => {
    if (turns.length > 0) dispatch({ type: "resume", turns, conversationId });
  }, []);

  useEffect(() => () => abortRef.current?.abort(), []);

  const cards = state.order.map((id) => state.byId[id]).filter(Boolean).sort((a, b) => blend(b) - blend(a));
  return { turns: state.turns, status: state.status, phase: state.phase, cards, reason: state.reason, send, resume };
}
