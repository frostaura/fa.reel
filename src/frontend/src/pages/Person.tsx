import { Link, useLocation, useParams } from "react-router-dom";
import { Star } from "lucide-react";
import { useGetPersonQuery, useRatePersonMutation, useUnratePersonMutation } from "../store/api";
import { profileUrl, posterUrl } from "../lib/tmdbImages";
import PredictedRatingBadge from "../components/rec/PredictedRatingBadge";

/** Actor/person page: who they are, your explicit rating, the derived affinity, filmography. */
export default function Person() {
  const { personId = "" } = useParams();
  const location = useLocation();
  const { data: person, isLoading } = useGetPersonQuery(personId, { skip: !personId });
  const [ratePerson] = useRatePersonMutation();
  const [unratePerson] = useUnratePersonMutation();

  if (isLoading || !person) {
    return <div className="fa-body text-fa-frost-dim py-10 text-center">Loading…</div>;
  }

  return (
    <div className="space-y-6">
      <header className="flex items-start gap-4">
        <div className="h-24 w-24 rounded-full overflow-hidden bg-fa-ink-3 shrink-0">
          {profileUrl(person.profilePath) ? (
            <img src={profileUrl(person.profilePath)!} alt={person.name} className="h-full w-full object-cover" />
          ) : (
            <div className="h-full w-full flex items-center justify-center text-fa-frost-dim">
              {person.name.split(" ").map((w) => w[0]).slice(0, 2).join("")}
            </div>
          )}
        </div>
        <div className="space-y-2">
          <h1 className="text-2xl font-light text-fa-frost-bright">{person.name}</h1>
          {person.department && <p className="fa-caption text-fa-frost-dim">{person.department}</p>}
          {person.derivedAffinity != null && (
            <p className="fa-caption text-fa-frost-dim">
              You rate their work <span className="text-fa-frost">{person.derivedAffinity.toFixed(1)}</span> on average
              {" "}across {person.ratedTitleCount} {person.ratedTitleCount === 1 ? "title" : "titles"}.
            </p>
          )}
        </div>
      </header>

      {/* Explicit rating control */}
      <section className="fa-card p-4 space-y-2">
        <p className="fa-overline text-fa-frost-dim flex items-center gap-1.5">
          <Star className="h-3.5 w-3.5" /> your rating
        </p>
        <div className="flex gap-1 flex-wrap">
          {Array.from({ length: 10 }, (_, i) => i + 1).map((value) => (
            <button
              key={value}
              onClick={() => ratePerson({ personId, rating: value })}
              className={`h-9 w-8 rounded-md border text-sm tabular-nums transition ${
                person.userRating === value
                  ? "border-fa-frost bg-fa-frost/20 text-fa-frost-bright"
                  : "border-fa-edge bg-fa-glass text-fa-frost hover:border-fa-frost/40"
              }`}
              data-testid="person-rate-value"
            >
              {value}
            </button>
          ))}
          {person.userRating != null && (
            <button onClick={() => unratePerson({ personId })} className="fa-button-ghost text-fa-frost-dim ml-2">
              Clear
            </button>
          )}
        </div>
        <p className="fa-caption text-fa-frost-dim/70">Rating someone you love nudges their titles up across your feed.</p>
      </section>

      {/* Filmography */}
      {person.filmography.length > 0 && (
        <section className="space-y-3">
          <h2 className="fa-section-title text-base">In your library &amp; beyond</h2>
          <div className="grid grid-cols-3 sm:grid-cols-5 md:grid-cols-6 gap-4">
            {person.filmography.map((t) => (
              <Link
                key={t.titleId}
                to={`/title/${t.mediaType.toLowerCase()}/${t.tmdbId}`}
                state={{ backgroundLocation: location }}
                className="group block focus:outline-none"
              >
                <div className="relative overflow-hidden rounded-lg transition-transform duration-200 group-hover:scale-[1.03]">
                  <div className="reel-poster">
                    {posterUrl(t.posterPath, "w185") && (
                      <img src={posterUrl(t.posterPath, "w185")!} alt={t.name} loading="lazy" className="h-full w-full object-cover" />
                    )}
                  </div>
                  {t.userRating != null ? (
                    <span className="absolute top-2 right-2 rounded-full bg-fa-ink/80 text-fa-frost-bright fa-caption px-1.5 py-0.5 flex items-center gap-0.5">
                      <Star className="h-2.5 w-2.5 fill-current" /> {t.userRating}
                    </span>
                  ) : t.predictedRating != null ? (
                    <div className="absolute top-2 right-2">
                      <PredictedRatingBadge rating={t.predictedRating} />
                    </div>
                  ) : null}
                </div>
                <p className="mt-2 fa-caption text-fa-frost-bright truncate" title={t.name}>{t.name}</p>
                <p className="fa-caption text-fa-frost-dim">{t.year ?? ""}</p>
              </Link>
            ))}
          </div>
        </section>
      )}
    </div>
  );
}
