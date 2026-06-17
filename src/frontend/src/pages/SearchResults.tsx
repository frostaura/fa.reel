import { useState } from "react";
import { Link, useLocation, useSearchParams } from "react-router-dom";
import { AlertTriangle, Sparkles } from "lucide-react";
import PosterImage from "../components/rec/PosterImage";
import PredictedRatingBadge from "../components/rec/PredictedRatingBadge";
import BrowseFilters from "../components/rec/BrowseFilters";
import { DEFAULT_BROWSE_FILTER, matchesMedia } from "../lib/browseFilter";
import { useAskReel } from "../lib/useAskReel";
import type { PhaseData } from "../lib/askReelStream";

/**
 * "Ask Reel" — the live discovery surface. Submitting streams the whole experience from
 * /api/search/ask: an engaging animation appears instantly and narrates the work ("scanning…
 * found 24… scoring…") while cards materialise and fill in with personal scores. Titles are
 * pulled from TMDB on the fly, so the catalogue never bounds the result.
 */
function narrate(phase: PhaseData | null, query: string): string {
  if (!phase) return `Searching the world for “${query}”…`;
  switch (phase.stage) {
    case "searching":
      return `Scanning the catalogue for “${query}”…`;
    case "discovered":
      return `Found ${phase.found} titles — matching them to your taste…`;
    case "embedding":
      return `Reading ${phase.found} plots…`;
    case "scoring":
      return `Scoring ${phase.found} picks for you…`;
    case "ranking":
      return `Ranking your ${phase.found} picks…`;
    default:
      return "Working…";
  }
}

export default function SearchResults() {
  const [params] = useSearchParams();
  const query = params.get("q") ?? "";
  const location = useLocation();
  const [filter, setFilter] = useState(DEFAULT_BROWSE_FILTER);
  const { status, phase, cards, reason } = useAskReel(query);

  const visible = cards.filter((c) => matchesMedia(c.mediaType, filter));
  const streaming = status === "streaming";

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-light text-fa-frost-bright flex items-center gap-2">
            <Sparkles className="h-5 w-5 text-fa-frost" /> Ask Reel
          </h1>
          <p className="fa-caption text-fa-frost-dim mt-1">
            “{query}”
            {status === "done" && reason && <span className="ml-2 text-fa-frost-dim/80">· {reason}</span>}
          </p>
        </div>
        {cards.length > 0 && <BrowseFilters value={filter} onChange={setFilter} showAvailability={false} />}
      </div>

      {streaming && (
        <div data-testid="askreel-banner" className="fa-card relative overflow-hidden p-4 flex items-center gap-4">
          <span className="relative flex h-9 w-9 items-center justify-center shrink-0">
            <span className="absolute inline-flex h-full w-full rounded-full bg-fa-frost/20 animate-ping" />
            <Sparkles className="h-5 w-5 text-fa-frost" />
          </span>
          <div className="flex-1 min-w-0">
            <p data-testid="askreel-narration" className="fa-body text-fa-frost-bright truncate">
              {narrate(phase, query)}
            </p>
            <div className="mt-2 h-1 w-full overflow-hidden rounded bg-fa-frost/10">
              <div
                className="h-full w-1/3 rounded bg-fa-frost/60"
                style={{ animation: "fa-progress-slide 1.2s ease-in-out infinite" }}
              />
            </div>
          </div>
          {phase && phase.found > 0 && (
            <span className="fa-caption text-fa-frost-dim shrink-0 tabular-nums">
              {phase.scored > 0 ? `${phase.scored} scored` : `${phase.found} found`}
            </span>
          )}
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
          <p className="fa-caption text-fa-frost-dim">
            Try different words — moods (“something fun”), genres (“dark thriller”), settings
            (“medieval”), or eras (“90s sci-fi”) all work.
          </p>
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
              <p className="mt-2 fa-body text-fa-frost-bright truncate" title={card.name}>
                {card.name}
              </p>
              <p className="fa-caption text-fa-frost-dim">{card.year ?? ""}</p>
              {card.why && (
                <p className="fa-caption text-fa-frost/80 truncate" title={card.why}>
                  {card.why}
                </p>
              )}
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
