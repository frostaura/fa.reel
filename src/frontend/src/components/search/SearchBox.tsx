import { useEffect, useRef, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { Eye, Search, Sparkles } from "lucide-react";
import { useSearchTypeaheadQuery } from "../../store/api";
import { posterUrl } from "../../lib/tmdbImages";
import PredictedRatingBadge from "../rec/PredictedRatingBadge";

/**
 * One box, two modes: typing streams instant typeahead matches (personal lens — predicted
 * ratings, watched badges, filters airtight); the pinned "Ask Reel" row (or Enter) runs
 * natural-language search. A hint, never a silent mode switch.
 */
export default function SearchBox() {
  const [query, setQuery] = useState("");
  const [debounced, setDebounced] = useState("");
  const [open, setOpen] = useState(false);
  const navigate = useNavigate();
  const location = useLocation();
  const boxRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handle = window.setTimeout(() => setDebounced(query.trim()), 220);
    return () => window.clearTimeout(handle);
  }, [query]);

  const { data, isFetching } = useSearchTypeaheadQuery(debounced, {
    skip: debounced.length < 2,
  });

  // Close on outside click.
  useEffect(() => {
    const onPointerDown = (event: PointerEvent) => {
      if (boxRef.current && !boxRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener("pointerdown", onPointerDown);
    return () => document.removeEventListener("pointerdown", onPointerDown);
  }, []);

  const askReel = () => {
    if (query.trim().length === 0) return;
    setOpen(false);
    navigate(`/search?q=${encodeURIComponent(query.trim())}`);
  };

  // ≥5 words or comparator phrasing promotes the Ask row — a hint, not a switch.
  const looksNatural =
    query.trim().split(/\s+/).length >= 5 || /\b(like|but|similar to|vibe|feels|without)\b/i.test(query);

  const showDropdown = open && debounced.length >= 2;

  return (
    <div ref={boxRef} className="relative w-full max-w-md" data-testid="search-box">
      <div className="relative">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-fa-frost-dim pointer-events-none" />
        <input
          value={query}
          onChange={(e) => {
            setQuery(e.target.value);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          onKeyDown={(e) => {
            if (e.key === "Enter") askReel();
            if (e.key === "Escape") setOpen(false);
          }}
          placeholder="Search — or ask for a vibe…"
          className="fa-input w-full pl-9 pr-3 h-9 text-sm"
          data-testid="search-input"
        />
      </div>

      {showDropdown && (
        <div className="absolute top-11 inset-x-0 z-50 rounded-lg border border-fa-edge bg-fa-ink-2/95 backdrop-blur-md shadow-2xl overflow-hidden reel-rise">
          {/* Ask Reel — pinned; promoted to top when the query reads like a sentence */}
          {looksNatural && <AskRow query={query} onAsk={askReel} promoted />}

          {isFetching && (data?.titles.length ?? 0) === 0 ? (
            <div className="px-4 py-3 fa-caption text-fa-frost-dim">searching…</div>
          ) : (
            (data?.titles ?? []).map((title) => (
              <Link
                key={title.titleId}
                to={`/title/${title.mediaType.toLowerCase()}/${title.tmdbId}`}
                state={{ backgroundLocation: location }}
                onClick={() => setOpen(false)}
                className="flex items-center gap-3 px-3 py-2 hover:bg-fa-glass transition"
                data-testid="typeahead-result"
              >
                <div className="w-8 h-12 rounded overflow-hidden bg-fa-ink-3 shrink-0">
                  {posterUrl(title.posterPath, "w185") && (
                    <img src={posterUrl(title.posterPath, "w185")!} alt="" loading="lazy" className="h-full w-full object-cover" />
                  )}
                </div>
                <div className="min-w-0 flex-1">
                  <p className="fa-body text-fa-frost-bright truncate">{title.name}</p>
                  <p className="fa-caption text-fa-frost-dim">
                    {title.year ?? ""} · {title.mediaType === "Movie" ? "film" : "series"}
                  </p>
                </div>
                {title.isFullyWatched && (
                  <span className="inline-flex items-center gap-1 fa-caption text-fa-frost-dim shrink-0">
                    <Eye className="h-3.5 w-3.5" /> seen
                  </span>
                )}
                {title.predictedRating != null && !title.isFullyWatched && (
                  <PredictedRatingBadge rating={title.predictedRating} />
                )}
              </Link>
            ))
          )}

          {!looksNatural && <AskRow query={query} onAsk={askReel} />}
        </div>
      )}
    </div>
  );
}

function AskRow({ query, onAsk, promoted = false }: { query: string; onAsk: () => void; promoted?: boolean }) {
  return (
    <button
      onClick={onAsk}
      className={`flex w-full items-center gap-3 px-3 py-2.5 text-left transition hover:bg-fa-frost/10 ${
        promoted ? "border-b border-fa-edge/50 bg-fa-frost/5" : "border-t border-fa-edge/50"
      }`}
      data-testid="ask-reel-row"
    >
      <Sparkles className="h-4 w-4 text-fa-frost shrink-0" />
      <span className="fa-body text-fa-frost truncate">
        Ask Reel: <span className="text-fa-frost-bright">“{query.trim()}”</span>
      </span>
    </button>
  );
}
