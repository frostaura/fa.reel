import { useEffect, useState } from "react";
import { Link, useLocation, useSearchParams } from "react-router-dom";
import { AlertTriangle, Mic, Send, Sparkles } from "lucide-react";
import PosterImage from "../components/rec/PosterImage";
import PredictedRatingBadge from "../components/rec/PredictedRatingBadge";
import BrowseFilters from "../components/rec/BrowseFilters";
import { DEFAULT_BROWSE_FILTER, matchesMedia } from "../lib/browseFilter";
import { useAskReelChat } from "../lib/useAskReelChat";
import { useSpeech } from "../lib/useSpeech";
import type { PhaseData } from "../lib/askReelStream";

/**
 * "Ask Reel" — a conversational discovery agent. The first turn comes from the search box (?q=);
 * from there it's a chat: Reel replies, streams personally-scored cards pulled live from TMDB,
 * re-orders them as hyper-personal LLM fit lands, and refines across turns ("only movies",
 * "less intense", "more like the first one") — never repeating a title.
 */
const REFINE_CHIPS = ["Only movies", "Only TV", "Something lighter", "More intense", "More like the first one"];

function narrate(phase: PhaseData | null): string {
  switch (phase?.stage) {
    case "searching":
      return "Scanning the catalogue…";
    case "discovered":
      return `Found ${phase.found} titles — matching them to your taste…`;
    case "embedding":
      return `Reading ${phase.found} plots…`;
    case "scoring":
      return `Scoring ${phase.found} picks for you…`;
    case "ranking":
      return "Ranking your picks…";
    default:
      return "Thinking…";
  }
}

export default function SearchResults() {
  const [params] = useSearchParams();
  const seed = params.get("q") ?? "";
  const location = useLocation();
  const [filter, setFilter] = useState(DEFAULT_BROWSE_FILTER);
  const [draft, setDraft] = useState("");
  const { turns, status, phase, cards, reason, send } = useAskReelChat();
  const speech = useSpeech((text) => send(text, false));

  // A search from the header box (?q=) starts a fresh conversation. `send` aborts any in-flight
  // stream and resets on fresh=true, so a dev StrictMode double-invoke simply restarts cleanly.
  useEffect(() => {
    if (seed) send(seed, true);
  }, [seed, send]);

  const visible = cards.filter((c) => matchesMedia(c.mediaType, filter));
  const streaming = status === "streaming";

  const submit = (text: string) => {
    if (!text.trim()) return;
    setDraft("");
    send(text, false);
  };

  return (
    <div className="space-y-5 pb-32">
      <h1 className="text-2xl font-light text-fa-frost-bright flex items-center gap-2">
        <Sparkles className="h-5 w-5 text-fa-frost" /> Ask Reel
      </h1>

      {/* Transcript */}
      <div className="space-y-3">
        {turns.map((turn, i) =>
          turn.role === "user" ? (
            <div key={i} className="flex justify-end">
              <p className="fa-body bg-fa-frost/15 text-fa-frost-bright rounded-2xl rounded-br-sm px-4 py-2 max-w-[80%]">
                {turn.text}
              </p>
            </div>
          ) : (
            <div key={i} className="flex items-start gap-3 max-w-[85%]" data-testid="askreel-reply">
              <Sparkles className="h-5 w-5 text-fa-frost shrink-0 mt-1" />
              <p className="fa-body text-fa-frost-bright">{turn.text}</p>
            </div>
          ),
        )}
      </div>

      {streaming && (
        <div data-testid="askreel-banner" className="fa-card relative overflow-hidden p-4 flex items-center gap-4">
          <span className="relative flex h-8 w-8 items-center justify-center shrink-0">
            <span className="absolute inline-flex h-full w-full rounded-full bg-fa-frost/20 animate-ping" />
            <Sparkles className="h-4 w-4 text-fa-frost" />
          </span>
          <div className="flex-1 min-w-0">
            <p data-testid="askreel-narration" className="fa-caption text-fa-frost-bright truncate">{narrate(phase)}</p>
            <div className="mt-2 h-1 w-full overflow-hidden rounded bg-fa-frost/10">
              <div className="h-full w-1/3 rounded bg-fa-frost/60" style={{ animation: "fa-progress-slide 1.2s ease-in-out infinite" }} />
            </div>
          </div>
          {phase && phase.found > 0 && (
            <span className="fa-caption text-fa-frost-dim shrink-0 tabular-nums">
              {phase.scored > 0 ? `${phase.scored} scored` : `${phase.found} found`}
            </span>
          )}
        </div>
      )}

      {/* Refine chips + media filter */}
      {cards.length > 0 && (
        <div className="flex flex-wrap items-center gap-2">
          {REFINE_CHIPS.map((chip) => (
            <button
              key={chip}
              data-testid="askreel-chip"
              onClick={() => send(chip, false)}
              disabled={streaming}
              className="fa-caption rounded-full border border-fa-frost/20 px-3 py-1 text-fa-frost-dim hover:text-fa-frost-bright hover:border-fa-frost/40 disabled:opacity-40 transition-colors"
            >
              {chip}
            </button>
          ))}
          <div className="ml-auto">
            <BrowseFilters value={filter} onChange={setFilter} showAvailability={false} />
          </div>
        </div>
      )}

      {status === "error" && cards.length === 0 ? (
        <div className="fa-card p-8 text-center max-w-xl mx-auto space-y-3" data-testid="semantic-error">
          <AlertTriangle className="h-6 w-6 text-fa-warning mx-auto" />
          <p className="fa-body text-fa-frost-bright">Search hit a snag</p>
          <p className="fa-caption text-fa-frost-dim">Reel couldn’t reach the search engine. It’s usually momentary.</p>
        </div>
      ) : status === "done" && visible.length === 0 ? (
        <div className="fa-card p-8 text-center max-w-xl mx-auto space-y-2" data-testid="semantic-empty">
          <Sparkles className="h-6 w-6 text-fa-frost-dim mx-auto" />
          <p className="fa-body text-fa-frost-bright">No matches for that one</p>
          <p className="fa-caption text-fa-frost-dim">Try a different angle — a mood, a vibe, or “more like &lt;something&gt;”.</p>
        </div>
      ) : (
        <div className="grid grid-cols-2 sm:grid-cols-4 md:grid-cols-6 gap-4" data-testid="semantic-results">
          {visible.map((card) => (
            <Link
              key={card.titleId}
              to={`/title/${card.mediaType.toLowerCase()}/${card.tmdbId}`}
              state={{ backgroundLocation: location }}
              className="group block focus:outline-none reel-rise"
            >
              <div className="relative overflow-hidden rounded-lg transition-transform duration-200 group-hover:scale-[1.03]">
                <PosterImage path={card.posterPath} alt={card.name} size="w185" />
                {card.predictedRating != null && (
                  <div className="absolute top-2 right-2 reel-rise">
                    <PredictedRatingBadge rating={card.predictedRating} />
                  </div>
                )}
              </div>
              <p className="mt-2 fa-body text-fa-frost-bright truncate" title={card.name}>{card.name}</p>
              <p className="fa-caption text-fa-frost-dim">{card.year ?? ""}</p>
              {card.why && (
                <p className="fa-caption text-fa-frost/80 line-clamp-2" title={card.why}>{card.why}</p>
              )}
            </Link>
          ))}
        </div>
      )}

      {reason && status === "done" && <p className="fa-caption text-fa-frost-dim text-center">{reason}</p>}

      {/* Composer */}
      <form
        onSubmit={(e) => { e.preventDefault(); submit(draft); }}
        className="fixed bottom-0 inset-x-0 z-20 border-t border-fa-frost/10 bg-background/95 backdrop-blur px-4 py-3"
      >
        <div className="max-w-3xl mx-auto flex items-center gap-2">
          <input
            data-testid="askreel-composer"
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            placeholder="Ask for more, or refine — “something funnier”, “more like the second one”…"
            className="flex-1 rounded-full bg-fa-frost/10 px-4 py-2 fa-body text-fa-frost-bright placeholder:text-fa-frost-dim/60 focus:outline-none focus:ring-1 focus:ring-fa-frost/40"
          />
          {speech.supported && (
            <button
              type="button"
              onClick={() => (speech.listening ? speech.stop() : speech.start())}
              disabled={streaming}
              aria-label="Voice search"
              data-testid="askreel-mic"
              className={`rounded-full p-2.5 transition-colors disabled:opacity-40 ${
                speech.listening ? "bg-fa-frost/40 text-fa-frost-bright animate-pulse" : "bg-fa-frost/10 text-fa-frost-dim hover:text-fa-frost-bright"
              }`}
            >
              <Mic className="h-4 w-4" />
            </button>
          )}
          <button
            type="submit"
            disabled={!draft.trim() || streaming}
            className="rounded-full bg-fa-frost/20 p-2.5 text-fa-frost-bright hover:bg-fa-frost/30 disabled:opacity-40 transition-colors"
            aria-label="Send"
          >
            <Send className="h-4 w-4" />
          </button>
        </div>
      </form>
    </div>
  );
}
