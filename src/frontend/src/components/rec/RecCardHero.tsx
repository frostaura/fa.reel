import { useState } from "react";
import { Link, useLocation } from "react-router-dom";
import type { FeedCard } from "../../store/feedTypes";
import { backdropUrl, posterUrl } from "../../lib/tmdbImages";
import PredictedRatingBadge from "./PredictedRatingBadge";
import { formatRuntime } from "../../lib/format";

/**
 * Tonight's-picks hero: 3–5 big confident picks in a snap carousel. Backdrop art with the
 * frost scrim carrying an oversized why-this — the answer to "what should I watch tonight".
 */
export default function RecCardHero({ cards }: { cards: FeedCard[] }) {
  const [active, setActive] = useState(0);
  const location = useLocation();

  if (cards.length === 0) {
    return null;
  }

  return (
    <section aria-label="Tonight's picks" className="space-y-3">
      <div className="flex gap-4 overflow-x-auto snap-x snap-mandatory scroll-smooth pb-1 -mx-1 px-1"
        onScroll={(e) => {
          const el = e.currentTarget;
          setActive(Math.round((el.scrollLeft / Math.max(1, el.scrollWidth - el.clientWidth)) * (cards.length - 1)));
        }}
      >
        {cards.map((card, index) => {
          const art = backdropUrl(card.backdropPath, "w1280") ?? posterUrl(card.posterPath, "w500");
          return (
            <Link
              key={card.titleId}
              to={`/title/${card.mediaType.toLowerCase()}/${card.tmdbId}`}
              state={{ backgroundLocation: location }}
              className="relative w-[min(92%,860px)] shrink-0 snap-center overflow-hidden rounded-2xl border border-fa-edge bg-fa-ink-2 aspect-[16/8.5] group focus:outline-none focus-visible:ring-2 focus-visible:ring-fa-frost/60"
              data-testid="hero-card"
            >
              {art && (
                <img
                  src={art}
                  alt=""
                  loading={index === 0 ? "eager" : "lazy"}
                  decoding="async"
                  className="absolute inset-0 h-full w-full object-cover transition-transform duration-500 group-hover:scale-[1.03]"
                />
              )}
              <div className="absolute inset-0 reel-scrim" />
              <div className="absolute inset-x-0 bottom-0 p-5 sm:p-7 space-y-2">
                <div className="flex items-center gap-3 flex-wrap">
                  <h2 className="text-xl sm:text-3xl font-light text-fa-frost-bright drop-shadow">{card.name}</h2>
                  <PredictedRatingBadge rating={card.predictedRating} size="lg" />
                </div>
                <p className="fa-caption text-fa-frost-dim">
                  {[card.year, formatRuntime(card.runtimeMinutes), card.genres.slice(0, 3).join(" / ")]
                    .filter(Boolean)
                    .join(" · ")}
                </p>
                <p className="fa-body sm:text-base text-fa-frost max-w-2xl leading-snug">{card.whyThis}</p>
              </div>
            </Link>
          );
        })}
      </div>
      {cards.length > 1 && (
        <div className="flex justify-center gap-1.5" aria-hidden>
          {cards.map((card, index) => (
            <span
              key={card.titleId}
              className={`h-1.5 rounded-full transition-all ${index === active ? "w-6 bg-fa-frost" : "w-1.5 bg-fa-frost/30"}`}
            />
          ))}
        </div>
      )}
    </section>
  );
}
