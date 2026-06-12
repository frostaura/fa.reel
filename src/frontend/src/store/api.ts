import { createApi, fetchBaseQuery } from "@reduxjs/toolkit/query/react";

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

/**
 * Auth rides in HttpOnly cookies (reel_at / reel_rt) — no headers to prepare; every request
 * just needs credentials included. 401s surface through query `error` states; RequireAuth
 * redirects to the landing page.
 */
export const api = createApi({
  reducerPath: "reelApi",
  baseQuery: fetchBaseQuery({ baseUrl: "/api", credentials: "include" }),
  tagTypes: ["Session", "Sync", "Feed", "TasteDna", "Lab"],
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
} = api;
