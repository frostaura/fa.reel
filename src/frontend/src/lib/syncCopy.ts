/**
 * Contextual copy for the header sync pill's tooltip — what the background job is actually
 * doing for the user, per job kind. Kinds arrive on `job-progress` / `job-completed` SSE
 * events; unknown kinds fall back to the generic explainer, so new pipeline jobs are safe
 * to ship backend-first.
 */
export interface SyncExplainer {
  title: string;
  lines: string[];
}

const EXPLAINERS: Record<string, SyncExplainer> = {
  ingest: {
    title: "First sync with Trakt",
    lines: [
      "Pulling everything you've ever watched and rated",
      "Movies, shows, episodes — the full history",
    ],
  },
  hydrate: {
    title: "Enriching your library",
    lines: [
      "Artwork and trailers for every title",
      "Global popularity — the baseline your model must beat",
      "Cast & crew graph that powers your affinities",
      "Fetched once, then shared across all of Reel",
    ],
  },
  delta: {
    title: "Syncing with Trakt",
    lines: [
      "Pulling your latest watches and ratings",
      "Runs automatically every few minutes",
    ],
  },
  reconcile: {
    title: "Nightly reconcile",
    lines: [
      "Full sweep against Trakt to repair any drift",
      "Keeps your queue and history exact",
    ],
  },
  train: {
    title: "Training your model",
    lines: [
      "Fitting a fresh model on your ratings",
      "Every prediction stays explainable",
    ],
  },
  feed: {
    title: "Building tonight's picks",
    lines: [
      "Scoring eligible titles with your model",
      "Ranking by predicted rating, freshness and variety",
    ],
  },
};

const FALLBACK: SyncExplainer = {
  title: "Background sync",
  lines: ["Keeping Reel current with your library"],
};

export function syncExplainer(kind: string | null | undefined): SyncExplainer {
  return (kind && EXPLAINERS[kind]) || FALLBACK;
}

/**
 * Backend JobKind (sync/status payload) → the short SSE kind vocabulary above. Lets a page
 * reload seed the pill from the authoritative status endpoint, not just live events.
 */
const JOB_KIND_TO_SSE: Record<string, string> = {
  FullIngest: "ingest",
  DeltaSync: "delta",
  FullReconcile: "reconcile",
  HydrateCatalog: "hydrate",
  Train: "train",
  BuildFeed: "feed",
};

export function jobKindToSseKind(jobKind: string | null | undefined): string | null {
  return (jobKind && JOB_KIND_TO_SSE[jobKind]) || null;
}
