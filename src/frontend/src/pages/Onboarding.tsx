import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { useDispatch, useSelector } from "react-redux";
import { useGetSessionQuery, useGetSyncStatusQuery, useUpdateSettingsMutation } from "../store/api";
import { useOnboardingStream } from "../lib/onboardingStream";
import { pollAdvance, type IngestKind } from "../store/onboardingSlice";
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

/**
 * M1 shell of the live build-up show: real SSE stages and counters rendered as staged text.
 * The full theatrics (odometer tickers, DNA assembly, frost-melt reveal) layer onto this same
 * slice/event contract in M3 — no rework, only presentation.
 */
export default function Onboarding() {
  const dispatch = useDispatch<AppDispatch>();
  const navigate = useNavigate();
  const onboarding = useSelector((s: RootState) => s.onboarding);
  const { data: session } = useGetSessionQuery();
  const [updateSettings] = useUpdateSettingsMutation();

  const streaming = onboarding.stage !== "reveal" && onboarding.stage !== "failed";
  useOnboardingStream(streaming);

  // Authoritative fallback: while on this page, poll sync status; poll data may advance the
  // show past a stalled SSE stream (the poll wins — fa.foresight contract).
  const { data: syncStatus } = useGetSyncStatusQuery(undefined, {
    pollingInterval: streaming ? 15_000 : 0,
  });
  useEffect(() => {
    if (syncStatus) dispatch(pollAdvance({ pipelineStage: syncStatus.pipelineStage }));
  }, [syncStatus, dispatch]);

  const enterApp = async () => {
    await updateSettings({ onboarded: true }).unwrap().catch(() => undefined);
    navigate("/home");
  };

  const counters = Object.entries(onboarding.counters) as [IngestKind, number][];

  return (
    <div className="min-h-screen flex items-center justify-center px-6">
      <div className="w-full max-w-2xl space-y-8 reel-rise">
        <div className="text-center space-y-2">
          <div className="text-3xl font-light tracking-wide text-fa-frost-bright">Reel</div>
          {session && (
            <p className="fa-caption text-fa-frost-dim">
              Hello, {onboarding.traktUser ?? session.traktSlug}
            </p>
          )}
          <p className="fa-body text-fa-frost">{STAGE_COPY[onboarding.stage]}</p>
          {onboarding.stalled && onboarding.stage !== "reveal" && (
            <p className="fa-caption text-fa-frost-dim animate-pulse">
              Still working — large libraries take a minute…
            </p>
          )}
        </div>

        {counters.length > 0 && (
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            {counters.map(([kind, found]) => (
              <div key={kind} className="fa-card px-4 py-3 text-center">
                <div className="fa-stat-value" key={found}>
                  <span className="fa-shimmer">{found.toLocaleString()}</span>
                </div>
                <div className="fa-stat-label">{COUNTER_LABELS[kind]}</div>
              </div>
            ))}
          </div>
        )}

        {onboarding.insights.length > 0 && (
          <div className="space-y-2">
            {onboarding.insights.slice(-4).map((insight) => (
              <p key={insight.id} className="fa-body text-fa-frost/90 reel-rise text-center">
                {insight.text}
              </p>
            ))}
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

        {onboarding.stage === "reveal" && (
          <div className="text-center">
            <button onClick={enterApp} className="fa-button-primary text-base px-6 py-3">
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
