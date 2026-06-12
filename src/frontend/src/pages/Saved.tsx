import { Link, useLocation } from "react-router-dom";
import { ExternalLink, ListChecks } from "lucide-react";
import { useGetSavedQuery } from "../store/api";
import PosterImage from "../components/rec/PosterImage";
import PredictedRatingBadge from "../components/rec/PredictedRatingBadge";

/** Save-for-later shelf + the managed "Reel — Up Next" Trakt list panel. */
export default function Saved() {
  const { data, isLoading } = useGetSavedQuery();
  const location = useLocation();

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between gap-4 flex-wrap">
        <h1 className="text-2xl font-light text-fa-frost-bright">Saved</h1>
        {data?.managedListUrl && (
          <a href={data.managedListUrl} target="_blank" rel="noreferrer" className="fa-button">
            <ListChecks className="h-4 w-4" />
            Reel — Up Next on Trakt
            <ExternalLink className="h-3.5 w-3.5" />
          </a>
        )}
      </div>

      <p className="fa-caption text-fa-frost-dim max-w-2xl">
        Everything you saved, synced to your Trakt watchlist and the managed{" "}
        <span className="text-fa-frost">Reel — Up Next</span> list — visible in Plex, Kodi, Infuse and
        every Trakt-connected app. Watch something and it leaves the queue by itself.
      </p>

      {isLoading ? (
        <div className="grid grid-cols-2 sm:grid-cols-4 md:grid-cols-6 gap-4">
          {Array.from({ length: 6 }).map((_, i) => (
            <div key={i} className="reel-poster reel-shimmer" />
          ))}
        </div>
      ) : (data?.items.length ?? 0) === 0 ? (
        <div className="fa-card p-8 text-center">
          <p className="fa-body text-fa-frost-dim">
            Nothing saved yet — hit “Save for later” on any pick and it lands here and on your Trakt queue.
          </p>
        </div>
      ) : (
        <div className="grid grid-cols-2 sm:grid-cols-4 md:grid-cols-6 gap-4">
          {data!.items.map((item) => (
            <Link
              key={item.titleId}
              to={`/title/${item.mediaType.toLowerCase()}/${item.tmdbId}`}
              state={{ backgroundLocation: location }}
              className="group block focus:outline-none"
            >
              <div className="relative overflow-hidden rounded-lg transition-transform duration-200 group-hover:scale-[1.03]">
                <PosterImage path={item.posterPath} alt={item.name} size="w185" />
                {item.predictedRating != null && (
                  <div className="absolute top-2 right-2">
                    <PredictedRatingBadge rating={item.predictedRating} />
                  </div>
                )}
              </div>
              <p className="mt-2 fa-body text-fa-frost-bright truncate" title={item.name}>
                {item.name}
              </p>
              <p className="fa-caption text-fa-frost-dim">
                {item.year ?? ""}
                {item.onManagedList ? " · on Up Next" : ""}
              </p>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
