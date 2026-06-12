import { Link, useLocation } from "react-router-dom";
import type { ContinueEntry } from "../../store/feedTypes";
import PosterImage from "../rec/PosterImage";
import { formatEpisodeRef } from "../../lib/format";

/** In-progress shows with the next episode front and centre, in resume-likelihood order. */
export default function ContinueWatchingRow({ entries }: { entries: ContinueEntry[] }) {
  const location = useLocation();

  if (entries.length === 0) {
    return null;
  }

  return (
    <section className="space-y-3">
      <h2 className="fa-section-title text-base">Continue watching</h2>
      <div className="flex gap-4 overflow-x-auto snap-x pb-2 -mx-1 px-1 [scrollbar-width:thin]">
        {entries.map((entry) => {
          const next = formatEpisodeRef(entry.nextEpisodeSeason, entry.nextEpisodeNumber);
          const pct = Math.round(entry.completionPct * 100);
          return (
            <Link
              key={entry.titleId}
              to={`/title/${entry.mediaType.toLowerCase()}/${entry.tmdbId}`}
              state={{ backgroundLocation: location }}
              className="group block w-40 shrink-0 snap-start focus:outline-none"
              data-testid="continue-card"
            >
              <div className="relative overflow-hidden rounded-lg transition-transform duration-200 group-hover:scale-[1.03]">
                <PosterImage path={entry.posterPath} alt={entry.name} size="w185" />
                <div className="absolute inset-x-0 bottom-0 h-1 bg-fa-ink/70">
                  <div className="h-full bg-fa-frost" style={{ width: `${pct}%` }} />
                </div>
              </div>
              <div className="mt-2 space-y-0.5">
                <p className="fa-body font-medium text-fa-frost-bright truncate" title={entry.name}>
                  {entry.name}
                </p>
                <p className="fa-caption text-fa-frost-dim">
                  {next ? `Next: ${next}` : `${entry.watchedEpisodes}/${entry.totalAired} episodes`}
                </p>
              </div>
            </Link>
          );
        })}
      </div>
    </section>
  );
}
