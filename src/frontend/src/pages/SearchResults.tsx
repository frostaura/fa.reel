import { Link, useLocation, useSearchParams } from "react-router-dom";
import { Sparkles } from "lucide-react";
import { useSearchSemanticQuery } from "../store/api";
import PosterImage from "../components/rec/PosterImage";
import PredictedRatingBadge from "../components/rec/PredictedRatingBadge";

/** The "Ask Reel" results surface — semantic search over the shared embedding space. */
export default function SearchResults() {
  const [params] = useSearchParams();
  const query = params.get("q") ?? "";
  const location = useLocation();
  const { data, isFetching } = useSearchSemanticQuery(query, { skip: query.length === 0 });

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-light text-fa-frost-bright flex items-center gap-2">
          <Sparkles className="h-5 w-5 text-fa-frost" /> Ask Reel
        </h1>
        <p className="fa-caption text-fa-frost-dim mt-1">“{query}”</p>
      </div>

      {isFetching ? (
        <div className="grid grid-cols-2 sm:grid-cols-4 md:grid-cols-6 gap-4">
          {Array.from({ length: 12 }).map((_, i) => (
            <div key={i} className="reel-poster reel-shimmer" />
          ))}
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
      ) : (
        <div className="grid grid-cols-2 sm:grid-cols-4 md:grid-cols-6 gap-4">
          {(data?.results ?? []).map((result) => (
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
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
