import { Link, useLocation } from "react-router-dom";
import type { FeedCard } from "../../store/feedTypes";
import PosterImage from "./PosterImage";
import PredictedRatingBadge from "./PredictedRatingBadge";
import { formatRuntime } from "../../lib/format";

/** Collapsed recommendation card: poster · meta line · why-this · predicted rating. */
export default function RecCard({ card }: { card: FeedCard }) {
  const location = useLocation();

  return (
    <Link
      to={`/title/${card.mediaType.toLowerCase()}/${card.tmdbId}`}
      state={{ backgroundLocation: location }}
      className="group block w-44 sm:w-48 shrink-0 focus:outline-none"
      data-testid="rec-card"
    >
      <div className="relative overflow-hidden rounded-lg transition-transform duration-200 group-hover:scale-[1.03] group-focus-visible:ring-2 group-focus-visible:ring-fa-frost/60">
        <PosterImage path={card.posterPath} alt={card.name} />
        <div className="absolute top-2 right-2">
          <PredictedRatingBadge rating={card.predictedRating} />
        </div>
      </div>
      <div className="mt-2 space-y-1">
        <p className="fa-body font-medium text-fa-frost-bright truncate" title={card.name}>
          {card.name}
        </p>
        <p className="fa-caption text-fa-frost-dim">
          {[card.year, formatRuntime(card.runtimeMinutes)].filter(Boolean).join(" · ")}
        </p>
        <p className="fa-caption text-fa-frost/80 line-clamp-2 leading-snug">{card.whyThis}</p>
      </div>
    </Link>
  );
}
