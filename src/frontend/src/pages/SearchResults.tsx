import { useState } from "react";
import { Link, useLocation, useSearchParams } from "react-router-dom";
import { AlertTriangle, Sparkles } from "lucide-react";
import { useSearchSemanticQuery } from "../store/api";
import PosterImage from "../components/rec/PosterImage";
import PredictedRatingBadge from "../components/rec/PredictedRatingBadge";
import BrowseFilters from "../components/rec/BrowseFilters";
import { DEFAULT_BROWSE_FILTER, matchesMedia } from "../lib/browseFilter";

/**
 * The "Ask Reel" results surface. Semantic (embedding-ranked) when the OpenAI key is
 * configured; otherwise the typo-tolerant lexical engine — every state renders something:
 * results, "no matches", an error card, or the warming-up notice. Never a blank page.
 */
export default function SearchResults() {
  const [params] = useSearchParams();
  const query = params.get("q") ?? "";
  const location = useLocation();
  const { data, isFetching, isError, refetch } = useSearchSemanticQuery(query, { skip: query.length === 0 });
  const [filter, setFilter] = useState(DEFAULT_BROWSE_FILTER);

  const results = (data?.results ?? []).filter((r) => matchesMedia(r.mediaType, filter));

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-light text-fa-frost-bright flex items-center gap-2">
            <Sparkles className="h-5 w-5 text-fa-frost" /> Ask Reel
          </h1>
          <p className="fa-caption text-fa-frost-dim mt-1">
            “{query}”
            {data?.mode === "lexical" && results.length > 0 && (
              <span className="ml-2 text-fa-frost-dim/80">· matched on concepts &amp; keywords</span>
            )}
          </p>
        </div>
        {(data?.results.length ?? 0) > 0 && <BrowseFilters value={filter} onChange={setFilter} showAvailability={false} />}
      </div>

      {isFetching ? (
        <div className="grid grid-cols-2 sm:grid-cols-4 md:grid-cols-6 gap-4">
          {Array.from({ length: 12 }).map((_, i) => (
            <div key={i} className="reel-poster reel-shimmer" />
          ))}
        </div>
      ) : isError ? (
        <div className="fa-card p-8 text-center max-w-xl mx-auto space-y-3" data-testid="semantic-error">
          <AlertTriangle className="h-6 w-6 text-fa-warning mx-auto" />
          <p className="fa-body text-fa-frost-bright">Search hit a snag</p>
          <p className="fa-caption text-fa-frost-dim">
            Reel couldn’t reach the search engine. It’s usually momentary.
          </p>
          <button onClick={() => refetch()} className="fa-button" data-testid="semantic-retry">
            Try again
          </button>
        </div>
      ) : data && !data.available ? (
        <div className="fa-card p-8 text-center max-w-xl mx-auto space-y-2" data-testid="semantic-unavailable">
          <Sparkles className="h-6 w-6 text-fa-frost-dim mx-auto" />
          <p className="fa-body text-fa-frost-bright">Natural-language search is warming up</p>
          <p className="fa-caption text-fa-frost-dim">{data.reason}</p>
          <p className="fa-caption text-fa-frost-dim">
            Meanwhile, the search box up top matches titles and people instantly.
          </p>
        </div>
      ) : results.length === 0 ? (
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
          {results.map((result) => (
            <Link
              key={result.titleId}
              to={`/title/${result.mediaType.toLowerCase()}/${result.tmdbId}`}
              state={{ backgroundLocation: location }}
              className="group block focus:outline-none"
            >
              <div className="relative overflow-hidden rounded-lg transition-transform duration-200 group-hover:scale-[1.03]">
                <PosterImage path={result.posterPath} alt={result.name} size="w185" />
                {result.predictedRating != null && (
                  <div className="absolute top-2 right-2">
                    <PredictedRatingBadge rating={result.predictedRating} />
                  </div>
                )}
              </div>
              <p className="mt-2 fa-body text-fa-frost-bright truncate" title={result.name}>
                {result.name}
              </p>
              <p className="fa-caption text-fa-frost-dim">{result.year ?? ""}</p>
              {(result.matchedOn?.length ?? 0) > 0 && (
                <p className="fa-caption text-fa-frost-dim/80 truncate capitalize">
                  {result.matchedOn!.slice(0, 3).join(" · ").replace(/-/g, " ")}
                </p>
              )}
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
