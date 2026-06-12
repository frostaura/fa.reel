import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useDispatch, useSelector } from "react-redux";
import { BarChart3, CalendarRange, Clapperboard, Sparkles, Tags, UserRound } from "lucide-react";
import { api, useGetSessionQuery, useGetSyncStatusQuery, useUpdateSettingsMutation } from "../store/api";
import { useOnboardingStream } from "../lib/onboardingStream";
import { useCountUp } from "../lib/useCountUp";
import { pollAdvance, type IngestKind, type Insight } from "../store/onboardingSlice";
import type { AppDispatch, RootState } from "../store";

const COUNTER_LABELS: Record<IngestKind, string> = {
  movies: "Movies",
  shows: "Shows",
  episodes: "Episodes",
  ratings: "Ratings",
};

const STAGE_COPY: Record<string, string> = {
  connecting: "Linking your Trakt profile…",
  ingesting: "Pulling in everything you've ever watched…",
  profiling: "Reading your taste…",
  fitting: "Teaching your model…",
  reveal: "Your taste DNA is ready.",
  failed: "Something went wrong.",
};

const INSIGHT_ICONS: Record<Insight["kind"], typeof Sparkles> = {
  library: Clapperboard,
  genre: Tags,
  era: CalendarRange,
  creator: UserRound,
  tone: Sparkles,
  stat: BarChart3,
};

function Ticker({ kind, value }: { kind: IngestKind; value: number }) {
  const display = useCountUp(value);
  return (
    <div className="fa-card px-4 py-3 text-center reel-rise">
      <div className="fa-stat-value">{display.toLocaleString()}</div>
      <div className="fa-stat-label">{COUNTER_LABELS[kind]}</div>
    </div>
  );
}

/**
 * The live build-up show: the wait IS the wow. Every number and insight on screen is a real
 * byproduct of the pipeline — never theatre. The reveal melts the frost veil into the feed.
 */
export default function Onboarding() {
  const dispatch = useDispatch<AppDispatch>();
  const navigate = useNavigate();
  const onboarding = useSelector((s: RootState) => s.onboarding);
  const { data: session } = useGetSessionQuery();
  const [updateSettings] = useUpdateSettingsMutation();
  const [melting, setMelting] = useState(false);

  const streaming = onboarding.stage !== "reveal" && onboarding.stage !== "failed";
  useOnboardingStream(streaming);

  // Authoritative fallback: the status poll can advance a stalled show (poll wins).
  const { data: syncStatus } = useGetSyncStatusQuery(undefined, {
    pollingInterval: streaming ? 15_000 : 0,
  });
  useEffect(() => {
    if (syncStatus) dispatch(pollAdvance({ pipelineStage: syncStatus.pipelineStage }));
  }, [syncStatus, dispatch]);

  // Prefetch the feed the moment the reveal is reachable — entry must feel instant.
  useEffect(() => {
    if (onboarding.stage === "fitting" || onboarding.stage === "reveal") {
      dispatch(api.util.prefetch("getFeed", undefined, { force: false }));
    }
  }, [onboarding.stage, dispatch]);

  const enterApp = async () => {
    setMelting(true);
    updateSettings({ onboarded: true });
    window.setTimeout(() => navigate("/home"), 620);
  };

  const counters = Object.entries(onboarding.counters) as [IngestKind, number][];

  return (
    <div className="min-h-screen flex items-center justify-center px-6 relative overflow-hidden">
      <div className={`w-full max-w-2xl space-y-8 ${melting ? "reel-melt" : ""}`}>
        <div className="text-center space-y-2">
          <div className="text-3xl font-light tracking-wide text-fa-frost-bright">Reel</div>
          {session && (
            <p className="fa-caption text-fa-frost-dim">
              Hello, {onboarding.traktUser ?? session.traktSlug}
            </p>
          )}
          <p className="fa-body text-fa-frost" data-testid="stage-copy">
            {STAGE_COPY[onboarding.stage]}
          </p>
          {onboarding.stalled && streaming && (
            <p className="fa-caption text-fa-frost-dim animate-pulse" data-testid="stall-notice">
              Still working — large libraries take a minute…
            </p>
          )}
        </div>

        {counters.length > 0 && (
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3" data-testid="tickers">
            {counters.map(([kind, found]) => (
              <Ticker key={kind} kind={kind} value={found} />
            ))}
          </div>
        )}

        {onboarding.insights.length > 0 && (
          <div className="space-y-2" data-testid="insights">
            {onboarding.insights.slice(-4).map((insight) => {
              const Icon = INSIGHT_ICONS[insight.kind] ?? Sparkles;
              return (
                <div key={insight.id} className="fa-card px-4 py-2.5 flex items-center gap-3 reel-rise">
                  <Icon className="h-4 w-4 text-fa-frost shrink-0" />
                  <p className="fa-body text-fa-frost/90">{insight.text}</p>
                </div>
              );
            })}
          </div>
        )}

        {onboarding.stage === "fitting" && onboarding.modelProgress && (
          <p className="fa-caption text-fa-frost-dim text-center">
            {onboarding.modelProgress.phase}
            {onboarding.modelProgress.total > 0 &&
              ` · ${onboarding.modelProgress.processed.toLocaleString()} / ${onboarding.modelProgress.total.toLocaleString()}`}
          </p>
        )}

        {streaming && (
          <div className="relative h-1 overflow-hidden rounded-full bg-fa-glass">
            <div className="absolute inset-y-0 rounded-full bg-fa-frost/60 fa-progress-slide" />
          </div>
        )}

        {onboarding.stage === "reveal" && !melting && (
          <div className="text-center space-y-4 reel-rise" data-testid="reveal">
            <div className="fa-card px-8 py-6 space-y-3 border-fa-frost/30">
              <Sparkles className="h-6 w-6 text-fa-frost mx-auto" />
              <p className="fa-section-title text-base">Your taste DNA is ready</p>
              <p className="fa-caption text-fa-frost-dim">
                Model trained on your ratings — every pick comes with the reason and a predicted rating.
              </p>
            </div>
            <button onClick={enterApp} className="fa-button-primary text-base px-6 py-3" data-testid="enter-app">
              See tonight&apos;s picks
            </button>
          </div>
        )}

        {onboarding.stage === "failed" && (
          <div className="fa-card border-fa-danger/40 px-6 py-4 text-center space-y-3">
            <p className="fa-body text-fa-danger">{onboarding.error}</p>
            <button onClick={() => window.location.reload()} className="fa-button">
              Retry sync
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
