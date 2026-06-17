/**
 * POST-SSE client for live "Ask Reel". EventSource is GET-only, so we read the streamed
 * `text/event-stream` body off a fetch ReadableStream and parse the frames ourselves. Each
 * frame is `event: <name>\ndata: <json>\n\n`. Events: phase → candidate → candidate-scored → done.
 */

export interface AskCard {
  titleId: string;
  mediaType: "Movie" | "Show";
  tmdbId: number | null;
  name: string;
  year: number | null;
  posterPath: string | null;
  genres: string[];
  similarity: number | null;
  predictedRating: number | null;
  /** Phase B: hyper-personal LLM fit + reason; absent until reranked. */
  fit?: number | null;
  why?: string | null;
}

export interface PhaseData {
  stage: string;
  found: number;
  scored: number;
}

export interface DoneData {
  results: AskCard[];
  reason: string | null;
}

export type AskEvent =
  | { event: "phase"; data: PhaseData }
  | { event: "conversation"; data: { id: string } }
  | { event: "candidate"; data: AskCard }
  | { event: "candidate-scored"; data: { titleId: string; predictedRating: number | null } }
  | { event: "candidate-reranked"; data: { titleId: string; fit: number | null; why: string | null } }
  | { event: "assistant-message"; data: { text: string } }
  | { event: "done"; data: DoneData };

export interface AskReelRequest {
  query: string;
  history?: { role: string; text: string }[];
  shownTmdbIds?: number[];
  conversationId?: string | null;
}

/** Streams /api/search/ask, invoking onEvent per parsed SSE frame. Resolves when the stream ends. */
export async function streamAskReel(
  request: AskReelRequest,
  onEvent: (ev: AskEvent) => void,
  signal: AbortSignal,
): Promise<void> {
  const res = await fetch("/api/search/ask", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
    credentials: "include",
    signal,
  });
  if (!res.ok || !res.body) {
    throw new Error(`ask failed: ${res.status}`);
  }

  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    let sep: number;
    while ((sep = buffer.indexOf("\n\n")) !== -1) {
      const frame = buffer.slice(0, sep);
      buffer = buffer.slice(sep + 2);
      const ev = parseFrame(frame);
      if (ev) onEvent(ev);
    }
  }
}

function parseFrame(frame: string): AskEvent | null {
  let event = "message";
  let data = "";
  for (const line of frame.split("\n")) {
    if (line.startsWith("event:")) event = line.slice(6).trim();
    else if (line.startsWith("data:")) data += line.slice(5).trim();
  }
  if (!data) return null;
  try {
    return { event, data: JSON.parse(data) } as AskEvent;
  } catch {
    return null;
  }
}
