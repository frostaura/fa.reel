import { createApi, fetchBaseQuery, type BaseQueryFn, type FetchArgs, type FetchBaseQueryError } from "@reduxjs/toolkit/query/react";
import type { ContinueEntry, FeedPayload } from "./feedTypes";
import type { SavedEntry, TitleDetailPayload } from "./titleTypes";

export interface TasteDnaPayload {
  userMean: number;
  contrarianScore: number;
  ratingsCount: number;
  hoursWatched: number;
  titlesWatched: number;
  showCompletionRate: number;
  topGenres: { genre: string; affinity: number; count: number }[];
  eras: { decade: number; affinity: number; count: number }[];
  creators: { name: string; department: string | null; profilePath: string | null; affinity: number; count: number }[];
  histogram: { rating: number; count: number }[];
  drift: { year: number; shares: Record<string, number> }[];
}

/** Pipeline stages as reported by the backend (Account.PipelineStage). */
export type PipelineStage =
  | "Linked"
  | "Ingesting"
  | "Extracting"
  | "Training"
  | "Evaluated"
  | "FeedReady"
  | "Degraded";

export type Tier = "Free" | "Paid" | "Founder";

export interface Session {
  accountId: string;
  displayName: string;
  traktSlug: string;
  avatarUrl: string | null;
  region: string;
  tier: Tier;
  pipelineStage: PipelineStage;
  onboarded: boolean;
  pinConfigured: boolean;
}

export interface TraktAuthStart {
  authorizeUrl: string;
  state: string;
}

export interface SyncJobSummary {
  kind: string;
  status: "Pending" | "Running" | "Succeeded" | "Failed" | "Cancelled";
  progressPct: number | null;
  progressMessage: string | null;
  enqueuedAt: string;
  completedAt: string | null;
}

export interface SyncStatus {
  pipelineStage: PipelineStage;
  connectionStatus: "Active" | "RefreshFailed" | "Revoked";
  lastDeltaSyncAt: string | null;
  lastFullReconcileAt: string | null;
  activeJob: SyncJobSummary | null;
  recentJobs: SyncJobSummary[];
  outboxPending: number;
  outboxDeadLetters: number;
}

export interface AccountSettings {
  region: string;
  onboarded: boolean;
}

export interface ModelEvalDetail {
  topRanked?: { titleId: string; rating: number; predicted: number; hit: boolean }[];
  featureImportance?: Record<string, number>;
  caveats?: string[];
}

export interface ModelEval {
  modelPrecisionAt10: number;
  baselinePrecisionAt10: number;
  relativeImprovement: number;
  rmse: number;
  mae: number;
  spearmanRho: number;
  holdoutPositiveCount: number;
  lowSample: boolean;
  passedGate: boolean;
  detail: ModelEvalDetail;
}

export interface ModelRun {
  id: string;
  iteration: number;
  configHash: string;
  splitAt: string;
  trainRowCount: number;
  holdoutRowCount: number;
  positiveThreshold: number;
  status: string;
  startedAt: string;
  eval: ModelEval | null;
}

export interface ModelMetrics {
  gate: {
    threshold: number;
    passed: boolean;
    latestLift: number | null;
    iterationsUsed: number;
    killCriterionAt: number;
  };
  activeArtifact: { version: number; algo: string; trainedAt: string } | null;
  runs: ModelRun[];
}

/**
 * Auth rides in HttpOnly cookies (reel_at / reel_rt) — no headers to prepare; every request
 * just needs credentials included. The access cookie lives 15 minutes: on a 401 the base
 * query silently rotates the session via /auth/refresh (single-flight) and retries once.
 * A refreshed access cookie also heals the SSE EventSource on its next auto-reconnect.
 * Terminal 401s surface through query `error` states; RequireAuth redirects to landing.
 */
const rawBaseQuery = fetchBaseQuery({ baseUrl: "/api", credentials: "include" });

let refreshInFlight: Promise<Response> | null = null;

const baseQueryWithReauth: BaseQueryFn<string | FetchArgs, unknown, FetchBaseQueryError> = async (
  args,
  queryApi,
  extraOptions
) => {
  let result = await rawBaseQuery(args, queryApi, extraOptions);

  const url = typeof args === "string" ? args : args.url;
  if (result.error?.status === 401 && !url.startsWith("auth/")) {
    refreshInFlight ??= fetch("/api/auth/refresh", { method: "POST", credentials: "include" });
    const refresh = await refreshInFlight.finally(() => {
      refreshInFlight = null;
    });
    if (refresh.ok) {
      result = await rawBaseQuery(args, queryApi, extraOptions);
    }
  }

  return result;
};

export const api = createApi({
  reducerPath: "reelApi",
  baseQuery: baseQueryWithReauth,
  tagTypes: ["Session", "Sync", "Feed", "TasteDna", "Lab", "Title", "Saved"],
  endpoints: (b) => ({
    getSession: b.query<Session, void>({
      query: () => "auth/me",
      providesTags: ["Session"],
    }),
    startTraktAuth: b.mutation<TraktAuthStart, void>({
      // POST: generates a fresh signed state server-side on each call.
      query: () => ({ url: "auth/trakt/start", method: "POST" }),
    }),
    exchangeTraktCode: b.mutation<Session, { code: string; state: string }>({
      query: (body) => ({ url: "auth/trakt/callback", method: "POST", body }),
      invalidatesTags: ["Session", "Sync"],
    }),
    logout: b.mutation<void, void>({
      query: () => ({ url: "auth/logout", method: "POST" }),
    }),
    getSyncStatus: b.query<SyncStatus, void>({
      query: () => "sync/status",
      providesTags: ["Sync"],
    }),
    triggerSync: b.mutation<void, void>({
      query: () => ({ url: "sync/now", method: "POST" }),
      invalidatesTags: ["Sync"],
    }),
    updateSettings: b.mutation<AccountSettings, Partial<AccountSettings>>({
      query: (body) => ({ url: "settings", method: "PUT", body }),
      invalidatesTags: ["Session"],
    }),
    getFeed: b.query<FeedPayload, void>({
      query: () => "feed",
      providesTags: ["Feed"],
    }),
    getContinueWatching: b.query<ContinueEntry[], void>({
      query: () => "feed/continue-watching",
      providesTags: ["Feed"],
    }),
    rebuildFeed: b.mutation<void, void>({
      query: () => ({ url: "feed/rebuild", method: "POST" }),
      invalidatesTags: ["Sync"],
    }),
    getTitle: b.query<TitleDetailPayload, { mediaType: string; tmdbId: number }>({
      query: ({ mediaType, tmdbId }) => `titles/${mediaType}/${tmdbId}`,
      providesTags: (_r, _e, arg) => [{ type: "Title", id: `${arg.mediaType}:${arg.tmdbId}` }],
    }),
    rateTitle: b.mutation<{ userRating: number }, { mediaType: string; tmdbId: number; rating: number; markWatched?: boolean }>({
      query: ({ mediaType, tmdbId, ...body }) => ({ url: `titles/${mediaType}/${tmdbId}/rating`, method: "POST", body }),
      async onQueryStarted({ mediaType, tmdbId, rating }, { dispatch, queryFulfilled }) {
        const patch = dispatch(
          api.util.updateQueryData("getTitle", { mediaType, tmdbId }, (draft) => {
            draft.userState.userRating = rating;
            draft.userState.isFullyWatched = true;
          })
        );
        try {
          await queryFulfilled;
          dispatch(api.util.invalidateTags(["TasteDna", "Saved"]));
        } catch {
          patch.undo();
        }
      },
    }),
    markNotInterested: b.mutation<void, { mediaType: string; tmdbId: number; reason?: string }>({
      query: ({ mediaType, tmdbId, reason }) => ({
        url: `titles/${mediaType}/${tmdbId}/reactions/not_interested`,
        method: "POST",
        body: { reason },
      }),
      async onQueryStarted({ mediaType, tmdbId }, { dispatch, queryFulfilled }) {
        const patch = dispatch(
          api.util.updateQueryData("getTitle", { mediaType, tmdbId }, (draft) => {
            draft.userState.notInterested = true;
          })
        );
        try {
          await queryFulfilled;
        } catch {
          patch.undo();
        }
      },
    }),
    undoNotInterested: b.mutation<void, { mediaType: string; tmdbId: number }>({
      query: ({ mediaType, tmdbId }) => ({ url: `titles/${mediaType}/${tmdbId}/reactions/not_interested`, method: "DELETE" }),
      async onQueryStarted({ mediaType, tmdbId }, { dispatch, queryFulfilled }) {
        const patch = dispatch(
          api.util.updateQueryData("getTitle", { mediaType, tmdbId }, (draft) => {
            draft.userState.notInterested = false;
          })
        );
        try {
          await queryFulfilled;
        } catch {
          patch.undo();
        }
      },
    }),
    saveForLater: b.mutation<void, { mediaType: string; tmdbId: number }>({
      query: ({ mediaType, tmdbId }) => ({ url: `titles/${mediaType}/${tmdbId}/reactions/save_for_later`, method: "POST" }),
      async onQueryStarted({ mediaType, tmdbId }, { dispatch, queryFulfilled }) {
        const patch = dispatch(
          api.util.updateQueryData("getTitle", { mediaType, tmdbId }, (draft) => {
            draft.userState.savedForLater = true;
          })
        );
        try {
          await queryFulfilled;
          dispatch(api.util.invalidateTags(["Saved"]));
        } catch {
          patch.undo();
        }
      },
    }),
    unsaveForLater: b.mutation<void, { mediaType: string; tmdbId: number }>({
      query: ({ mediaType, tmdbId }) => ({ url: `titles/${mediaType}/${tmdbId}/reactions/save_for_later`, method: "DELETE" }),
      async onQueryStarted({ mediaType, tmdbId }, { dispatch, queryFulfilled }) {
        const patch = dispatch(
          api.util.updateQueryData("getTitle", { mediaType, tmdbId }, (draft) => {
            draft.userState.savedForLater = false;
          })
        );
        try {
          await queryFulfilled;
          dispatch(api.util.invalidateTags(["Saved"]));
        } catch {
          patch.undo();
        }
      },
    }),
    getSaved: b.query<{ managedListUrl: string | null; items: SavedEntry[] }, void>({
      query: () => "saved",
      providesTags: ["Saved"],
    }),
    getTasteDna: b.query<TasteDnaPayload, void>({
      query: () => "taste",
      providesTags: ["TasteDna"],
    }),
    getModelMetrics: b.query<ModelMetrics, void>({
      query: () => "metrics/model",
      providesTags: ["Lab"],
    }),
    trainModel: b.mutation<void, void>({
      query: () => ({ url: "metrics/model/train", method: "POST" }),
      invalidatesTags: ["Lab", "Sync"],
    }),
  }),
});

export const {
  useGetSessionQuery,
  useStartTraktAuthMutation,
  useExchangeTraktCodeMutation,
  useLogoutMutation,
  useGetSyncStatusQuery,
  useTriggerSyncMutation,
  useUpdateSettingsMutation,
  useGetModelMetricsQuery,
  useTrainModelMutation,
  useGetFeedQuery,
  useGetContinueWatchingQuery,
  useRebuildFeedMutation,
  useGetTitleQuery,
  useRateTitleMutation,
  useMarkNotInterestedMutation,
  useUndoNotInterestedMutation,
  useSaveForLaterMutation,
  useUnsaveForLaterMutation,
  useGetSavedQuery,
  useGetTasteDnaQuery,
} = api;
