import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Share2 } from "lucide-react";
import { toPng } from "html-to-image";
import { useGetTasteDnaQuery } from "../store/api";
import { profileUrl } from "../lib/tmdbImages";

const FROST = "#A4D4F4";
const FROST_DIM = "#5C8AB4";
const GRID = "rgba(164,212,244,0.12)";
const DRIFT_COLORS = ["#A4D4F4", "#7CE3B6", "#F6C667", "#C4B5FD", "#FDA4AF"];

function ChartTip({ active, payload, label }: { active?: boolean; payload?: { name: string; value: number }[]; label?: string }) {
  if (!active || !payload?.length) return null;
  return (
    <div className="rounded-md border border-fa-edge bg-fa-ink/95 backdrop-blur px-3 py-2">
      {label != null && <p className="fa-overline text-fa-frost-dim mb-1">{label}</p>}
      {payload.map((entry) => (
        <p key={entry.name} className="fa-caption text-fa-frost">
          {entry.name}: <span className="text-fa-frost-bright tabular-nums">{Number(entry.value).toFixed(2)}</span>
        </p>
      ))}
    </div>
  );
}

/** The free hook: who you are, in data. Built screenshot-clean — this page is the viral loop. */
export default function TasteDna() {
  const { data } = useGetTasteDnaQuery();
  const cardRef = useRef<HTMLDivElement>(null);
  const [sharing, setSharing] = useState(false);

  if (!data) {
    return <div className="h-72 rounded-2xl reel-shimmer" />;
  }

  const driftData = data.drift.map((d) => ({ year: d.year, ...d.shares }));
  const driftGenres = data.topGenres.slice(0, 5).map((g) => g.genre);

  // Export a shareable PNG. Excludes the avatar grid (cross-origin TMDB images would taint the
  // canvas) and any stray <img>; the radar + stats + branding are the shareable core.
  const share = async () => {
    if (!cardRef.current) return;
    setSharing(true);
    try {
      const dataUrl = await toPng(cardRef.current, {
        backgroundColor: "#06121F",
        pixelRatio: 2,
        filter: (node) => {
          const el = node as HTMLElement;
          return el.tagName !== "IMG" && el.dataset?.shareExclude === undefined;
        },
      });
      const link = document.createElement("a");
      link.download = "my-reel-taste-dna.png";
      link.href = dataUrl;
      link.click();
    } catch {
      // Capture can fail on some browsers — degrade silently.
    } finally {
      setSharing(false);
    }
  };

  return (
    <div ref={cardRef} className="space-y-8">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-light text-fa-frost-bright">Your taste DNA</h1>
          <p className="fa-caption text-fa-frost-dim mt-1">
            computed from {data.ratingsCount.toLocaleString()} ratings · always yours, always free
          </p>
        </div>
        <button
          onClick={share}
          disabled={sharing}
          data-share-exclude
          data-testid="dna-share"
          className="fa-button-ghost flex items-center gap-2 shrink-0 disabled:opacity-50"
        >
          <Share2 className="h-4 w-4" /> {sharing ? "Rendering…" : "Share card"}
        </button>
      </div>

      {/* Headline stats */}
      <section className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <div className="fa-card px-4 py-3">
          <p className="fa-stat-label">hours watched</p>
          <p className="fa-stat-value">{Math.round(data.hoursWatched).toLocaleString()}</p>
        </div>
        <div className="fa-card px-4 py-3">
          <p className="fa-stat-label">titles</p>
          <p className="fa-stat-value">{data.titlesWatched.toLocaleString()}</p>
        </div>
        <div className="fa-card px-4 py-3">
          <p className="fa-stat-label">show completion</p>
          <p className="fa-stat-value">{Math.round(data.showCompletionRate * 100)}%</p>
        </div>
        <div className="fa-card px-4 py-3">
          <p className="fa-stat-label">contrarian score</p>
          <p className="fa-stat-value">
            {data.contrarianScore > 0 ? "+" : ""}
            {data.contrarianScore.toFixed(1)}
          </p>
          <p className="fa-caption text-fa-frost-dim">{data.contrarianScore > 0.3 ? "kinder than the crowd" : data.contrarianScore < -0.3 ? "harsher than the crowd" : "with the crowd"}</p>
        </div>
      </section>

      <div className="grid lg:grid-cols-2 gap-6">
        {/* Genre fingerprint */}
        <section className="fa-card p-5">
          <h2 className="fa-section-title mb-3">Genre fingerprint</h2>
          <div className="space-y-2.5">
            {data.topGenres.map((g) => (
              <div key={g.genre} className="flex items-center gap-3">
                <span className="fa-body text-fa-frost w-36 truncate capitalize">{g.genre.replace("-", " ")}</span>
                <div className="flex-1 h-2.5 rounded-full bg-fa-glass overflow-hidden">
                  <div
                    className="h-full rounded-full bg-fa-frost/70"
                    style={{ width: `${Math.max(6, ((g.affinity - 5) / 5) * 100)}%` }}
                  />
                </div>
                <span className="fa-caption text-fa-frost-bright tabular-nums w-8 text-right">{g.affinity.toFixed(1)}</span>
                <span className="fa-caption text-fa-frost-dim tabular-nums w-12 text-right">{g.count}×</span>
              </div>
            ))}
          </div>
        </section>

        {/* Eras */}
        <section className="fa-card p-5">
          <h2 className="fa-section-title mb-2">Eras</h2>
          <ResponsiveContainer width="100%" height={280}>
            <BarChart data={data.eras} barCategoryGap="25%">
              <CartesianGrid stroke={GRID} vertical={false} />
              <XAxis dataKey="decade" type="category" tick={{ fill: FROST_DIM, fontSize: 11 }} tickFormatter={(d) => `${d}s`} />
              <YAxis domain={[0, 10]} tick={{ fill: FROST_DIM, fontSize: 11 }} width={28} />
              <Tooltip content={<ChartTip />} cursor={{ fill: "rgba(164,212,244,0.06)" }} />
              <Bar dataKey="affinity" fill={FROST} radius={[4, 4, 0, 0]} name="affinity" />
            </BarChart>
          </ResponsiveContainer>
        </section>
      </div>

      {/* Taste drift */}
      {driftData.length > 1 && (
        <section className="fa-card p-5">
          <h2 className="fa-section-title mb-2">Taste drift</h2>
          <p className="fa-caption text-fa-frost-dim mb-3">share of your loved ratings per genre, year by year</p>
          <ResponsiveContainer width="100%" height={240}>
            <LineChart data={driftData}>
              <CartesianGrid stroke={GRID} vertical={false} />
              <XAxis dataKey="year" type="category" tick={{ fill: FROST_DIM, fontSize: 11 }} />
              <YAxis tickFormatter={(v) => `${Math.round(v * 100)}%`} tick={{ fill: FROST_DIM, fontSize: 11 }} width={40} />
              <Tooltip content={<ChartTip />} />
              <Legend wrapperStyle={{ fontSize: 11, color: FROST_DIM }} />
              {driftGenres.map((genre, index) => (
                <Line
                  key={genre}
                  dataKey={genre}
                  stroke={DRIFT_COLORS[index]}
                  strokeWidth={2}
                  dot={false}
                  name={genre.replace("-", " ")}
                />
              ))}
            </LineChart>
          </ResponsiveContainer>
        </section>
      )}

      <div className="grid lg:grid-cols-2 gap-6" data-share-exclude>
        {/* Creators */}
        <section className="fa-card p-5">
          <h2 className="fa-section-title mb-3">Creator affinities</h2>
          <div className="space-y-2.5">
            {data.creators.slice(0, 8).map((creator) => (
              <div key={creator.name} className="flex items-center gap-3">
                <div className="h-8 w-8 rounded-full overflow-hidden bg-fa-ink-3 shrink-0">
                  {profileUrl(creator.profilePath) ? (
                    <img src={profileUrl(creator.profilePath)!} alt="" loading="lazy" className="h-full w-full object-cover" />
                  ) : (
                    <div className="h-full w-full flex items-center justify-center fa-caption text-fa-frost-dim">
                      {creator.name[0]}
                    </div>
                  )}
                </div>
                <span className="fa-body text-fa-frost w-40 truncate">{creator.name}</span>
                <div className="flex-1 h-2 rounded-full bg-fa-glass overflow-hidden">
                  <div className="h-full rounded-full bg-fa-frost/70" style={{ width: `${(creator.affinity / 10) * 100}%` }} />
                </div>
                <span className="fa-caption text-fa-frost-bright tabular-nums w-8 text-right">{creator.affinity.toFixed(1)}</span>
              </div>
            ))}
          </div>
        </section>

        {/* Top rated actors — explicit person ratings */}
        {data.topActors.length > 0 && (
          <section className="fa-card p-5" data-testid="top-actors">
            <h2 className="fa-section-title mb-3">Top rated actors</h2>
            <div className="space-y-2.5">
              {data.topActors.map((actor) => (
                <Link key={actor.personId} to={`/person/${actor.personId}`} className="flex items-center gap-3 group">
                  <div className="h-8 w-8 rounded-full overflow-hidden bg-fa-ink-3 shrink-0">
                    {profileUrl(actor.profilePath) ? (
                      <img src={profileUrl(actor.profilePath)!} alt="" loading="lazy" className="h-full w-full object-cover" />
                    ) : (
                      <div className="h-full w-full flex items-center justify-center fa-caption text-fa-frost-dim">
                        {actor.name[0]}
                      </div>
                    )}
                  </div>
                  <span className="fa-body text-fa-frost group-hover:text-fa-frost-bright w-40 truncate">{actor.name}</span>
                  <div className="flex-1 h-2 rounded-full bg-fa-glass overflow-hidden">
                    <div className="h-full rounded-full bg-fa-frost/70" style={{ width: `${(actor.rating / 10) * 100}%` }} />
                  </div>
                  <span className="fa-caption text-fa-frost-bright tabular-nums w-8 text-right">{actor.rating}</span>
                </Link>
              ))}
            </div>
          </section>
        )}

        {/* Ratings histogram */}
        <section className="fa-card p-5">
          <h2 className="fa-section-title mb-2">How you rate</h2>
          <ResponsiveContainer width="100%" height={240}>
            <BarChart data={data.histogram}>
              <CartesianGrid stroke={GRID} vertical={false} />
              <XAxis dataKey="rating" tick={{ fill: FROST_DIM, fontSize: 11 }} />
              <YAxis tick={{ fill: FROST_DIM, fontSize: 11 }} width={36} />
              <Tooltip content={<ChartTip />} cursor={{ fill: "rgba(164,212,244,0.06)" }} />
              <Bar dataKey="count" fill={FROST} radius={[4, 4, 0, 0]} name="ratings" />
            </BarChart>
          </ResponsiveContainer>
          <p className="fa-caption text-fa-frost-dim text-center">your mean: {data.userMean.toFixed(1)}</p>
        </section>
      </div>
    </div>
  );
}
