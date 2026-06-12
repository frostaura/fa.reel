import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { Bookmark, BookmarkCheck, Eye, EyeOff, Play, Undo2, X } from "lucide-react";
import {
  useGetTitleQuery,
  useMarkNotInterestedMutation,
  useRateTitleMutation,
  useSaveForLaterMutation,
  useUndoNotInterestedMutation,
  useUnsaveForLaterMutation,
} from "../store/api";
import { backdropUrl, profileUrl } from "../lib/tmdbImages";
import PosterImage from "../components/rec/PosterImage";
import PredictedRatingBadge from "../components/rec/PredictedRatingBadge";
import { formatRuntime } from "../lib/format";
import { Popover, PopoverContent, PopoverTrigger } from "../components/ui/popover";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "../components/ui/dropdown-menu";

/** Humanize a model feature name for the breakdown bars. */
function featureLabel(feature: string): string {
  if (feature.startsWith("genre:")) return feature.slice(6).replace("-", " ");
  const map: Record<string, string> = {
    castAffinity: "cast you rate highly",
    lovedCastCount: "favourite cast members",
    directorAffinity: "director affinity",
    writerAffinity: "writer affinity",
    userGenreAffinity: "your genre taste",
    genreDrift: "recent genre streak",
    decadeAffinity: "your favourite era",
    traktRating: "crowd rating",
    tmdbPopularityLog: "popularity",
    contrarianAdjustedRating: "your contrarian lens",
    showEngagement: "your episode ratings",
    releaseAgeLog: "release recency",
    runtimeMinutes: "runtime",
    certificationOrdinal: "maturity rating",
    isShow: "series format",
    traktVotesLog: "vote volume",
  };
  return map[feature] ?? feature;
}

export default function TitleDetail() {
  const { mediaType = "movie", tmdbId = "0" } = useParams();
  const navigate = useNavigate();
  const args = { mediaType, tmdbId: Number(tmdbId) };
  const { data: title, isLoading } = useGetTitleQuery(args);

  const [rate] = useRateTitleMutation();
  const [notInterested] = useMarkNotInterestedMutation();
  const [undoNotInterested] = useUndoNotInterestedMutation();
  const [save] = useSaveForLaterMutation();
  const [unsave] = useUnsaveForLaterMutation();
  const [trailerOpen, setTrailerOpen] = useState(false);
  const [ratePickerOpen, setRatePickerOpen] = useState(false);

  const close = () => navigate(-1);

  const trailerKey = title?.trailerUrl?.match(/[?&]v=([\w-]+)/)?.[1];

  return (
    <div
      className="fixed inset-0 z-50 overflow-y-auto bg-fa-ink/75 backdrop-blur-sm"
      onClick={close}
      data-testid="title-detail"
    >
      <div
        className="relative mx-auto my-6 w-[min(94vw,880px)] overflow-hidden rounded-2xl border border-fa-edge bg-fa-ink-2 shadow-2xl reel-rise"
        onClick={(e) => e.stopPropagation()}
      >
        <button onClick={close} aria-label="Close" className="absolute right-3 top-3 z-20 fa-button-ghost rounded-full p-2 bg-fa-ink/60">
          <X className="h-5 w-5" />
        </button>

        {isLoading || !title ? (
          <div className="aspect-[16/8] reel-shimmer" />
        ) : (
          <>
            {/* Header art / trailer */}
            <div className="relative aspect-[16/8] bg-fa-ink-3">
              {trailerOpen && trailerKey ? (
                <iframe
                  src={`https://www.youtube-nocookie.com/embed/${trailerKey}?autoplay=1`}
                  title={`${title.name} trailer`}
                  allow="autoplay; encrypted-media"
                  allowFullScreen
                  className="absolute inset-0 h-full w-full"
                />
              ) : (
                <>
                  {backdropUrl(title.backdropPath, "w1280") && (
                    <img src={backdropUrl(title.backdropPath, "w1280")!} alt="" className="absolute inset-0 h-full w-full object-cover" />
                  )}
                  <div className="absolute inset-0 reel-scrim" />
                  {trailerKey && (
                    <button
                      onClick={() => setTrailerOpen(true)}
                      className="absolute inset-0 m-auto h-16 w-16 rounded-full border border-fa-frost/50 bg-fa-ink/70 backdrop-blur flex items-center justify-center transition hover:scale-110"
                      aria-label="Play trailer"
                    >
                      <Play className="h-7 w-7 text-fa-frost-bright translate-x-0.5" />
                    </button>
                  )}
                  <div className="absolute bottom-0 inset-x-0 p-5 flex items-end gap-4">
                    <div className="w-24 shrink-0 hidden sm:block drop-shadow-xl">
                      <PosterImage path={title.posterPath} alt={title.name} size="w185" />
                    </div>
                    <div className="space-y-1.5">
                      <div className="flex items-center gap-3 flex-wrap">
                        <h1 className="text-2xl sm:text-3xl font-light text-fa-frost-bright">{title.name}</h1>
                        {title.prediction && <PredictedRatingBadge rating={title.prediction.predictedRating} size="lg" />}
                      </div>
                      <p className="fa-caption text-fa-frost-dim">
                        {[
                          title.year,
                          formatRuntime(title.runtimeMinutes),
                          title.certification,
                          title.network,
                          title.genres.slice(0, 3).join(" / "),
                        ]
                          .filter(Boolean)
                          .join(" · ")}
                      </p>
                    </div>
                  </div>
                </>
              )}
            </div>

            <div className="p-5 sm:p-6 space-y-6">
              {/* Reactions */}
              <div className="flex items-center gap-2 flex-wrap" data-testid="reaction-bar">
                <Popover open={ratePickerOpen} onOpenChange={setRatePickerOpen}>
                  <PopoverTrigger asChild>
                    <button className={title.userState.userRating ? "fa-button-primary" : "fa-button"}>
                      <Eye className="h-4 w-4" />
                      {title.userState.userRating ? `Seen it — you rated ${title.userState.userRating}` : "Seen it + rate"}
                    </button>
                  </PopoverTrigger>
                  <PopoverContent align="start" className="w-auto">
                    <p className="fa-overline text-fa-frost-dim mb-2">one tap — seen &amp; rated</p>
                    <div className="flex gap-1">
                      {Array.from({ length: 10 }, (_, i) => i + 1).map((value) => (
                        <button
                          key={value}
                          onClick={() => {
                            rate({ ...args, rating: value });
                            setRatePickerOpen(false);
                          }}
                          className={`h-9 w-8 rounded-md border text-sm tabular-nums transition ${
                            title.userState.userRating === value
                              ? "border-fa-frost bg-fa-frost/25 text-fa-frost-bright"
                              : "border-fa-edge bg-fa-glass text-fa-frost hover:border-fa-frost/50 hover:bg-fa-frost/15"
                          }`}
                          data-testid={`rate-${value}`}
                        >
                          {value}
                        </button>
                      ))}
                    </div>
                  </PopoverContent>
                </Popover>

                <button
                  onClick={() =>
                    title.userState.savedForLater ? unsave(args) : save(args)
                  }
                  className={title.userState.savedForLater ? "fa-button-primary" : "fa-button"}
                  data-testid="save-button"
                >
                  {title.userState.savedForLater ? <BookmarkCheck className="h-4 w-4" /> : <Bookmark className="h-4 w-4" />}
                  {title.userState.savedForLater ? "Saved — on your Up Next" : "Save for later"}
                </button>

                {title.userState.notInterested ? (
                  <button onClick={() => undoNotInterested(args)} className="fa-button border-fa-danger/40 text-fa-danger">
                    <Undo2 className="h-4 w-4" /> Not interested — undo
                  </button>
                ) : (
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <button className="fa-button-ghost" data-testid="not-interested">
                        <EyeOff className="h-4 w-4" /> Not interested
                      </button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="start">
                      <DropdownMenuItem onClick={() => notInterested(args)}>Just not for me</DropdownMenuItem>
                      <DropdownMenuItem onClick={() => notInterested({ ...args, reason: "genre" })}>Not my genre</DropdownMenuItem>
                      <DropdownMenuItem onClick={() => notInterested({ ...args, reason: "seen-enough" })}>Seen enough like this</DropdownMenuItem>
                      <DropdownMenuItem onClick={() => notInterested({ ...args, reason: "tone" })}>Wrong tone</DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                )}
              </div>

              {/* Synopsis */}
              {title.overview && <p className="fa-body text-fa-frost leading-relaxed max-w-3xl">{title.overview}</p>}

              {/* Prediction breakdown */}
              {title.prediction && title.prediction.contributions.length > 0 && (
                <section className="space-y-2">
                  <h2 className="fa-overline text-fa-frost-dim">why {title.prediction.predictedRating.toFixed(1)} for you</h2>
                  <div className="space-y-1.5 max-w-xl">
                    {title.prediction.contributions.slice(0, 5).map((contribution) => {
                      const max = Math.max(...title.prediction!.contributions.map((c) => Math.abs(c.value))) || 1;
                      const positive = contribution.value >= 0;
                      return (
                        <div key={contribution.feature} className="flex items-center gap-3">
                          <span className="fa-caption text-fa-frost w-44 truncate">{featureLabel(contribution.feature)}</span>
                          <div className="flex-1 h-2 rounded-full bg-fa-glass overflow-hidden">
                            <div
                              className={`h-full rounded-full ${positive ? "bg-fa-success/70" : "bg-fa-danger/60"}`}
                              style={{ width: `${Math.max(5, (Math.abs(contribution.value) / max) * 100)}%` }}
                            />
                          </div>
                          <span className="fa-caption text-fa-frost-dim tabular-nums w-12 text-right">
                            {contribution.value >= 0 ? "+" : ""}
                            {contribution.value.toFixed(1)}
                          </span>
                        </div>
                      );
                    })}
                  </div>
                </section>
              )}

              {/* Credits */}
              {(title.directors.length > 0 || title.cast.length > 0) && (
                <section className="space-y-3">
                  {title.directors.length > 0 && (
                    <p className="fa-caption text-fa-frost-dim">
                      Directed by <span className="text-fa-frost">{title.directors.map((d) => d.name).join(", ")}</span>
                    </p>
                  )}
                  {title.cast.length > 0 && (
                    <div className="flex gap-3 overflow-x-auto pb-2 [scrollbar-width:thin]">
                      {title.cast.map((member) => (
                        <div key={member.personId} className="w-20 shrink-0 text-center space-y-1">
                          <div className="h-20 w-20 rounded-full overflow-hidden bg-fa-ink-3 mx-auto">
                            {profileUrl(member.profilePath) ? (
                              <img src={profileUrl(member.profilePath)!} alt={member.name} loading="lazy" className="h-full w-full object-cover" />
                            ) : (
                              <div className="h-full w-full flex items-center justify-center fa-caption text-fa-frost-dim">
                                {member.name.split(" ").map((w) => w[0]).slice(0, 2).join("")}
                              </div>
                            )}
                          </div>
                          <p className="fa-caption text-fa-frost truncate" title={member.name}>{member.name}</p>
                          {member.character && <p className="fa-caption text-fa-frost-dim/70 truncate">{member.character}</p>}
                        </div>
                      ))}
                    </div>
                  )}
                </section>
              )}
            </div>
          </>
        )}
      </div>
    </div>
  );
}
