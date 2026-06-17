/** Shared feed payload types (mirror FeedEndpoints DTOs). */
export interface FeedCard {
  titleId: string;
  mediaType: "Movie" | "Show";
  tmdbId: number | null;
  name: string;
  year: number | null;
  runtimeMinutes: number | null;
  posterPath: string | null;
  backdropPath: string | null;
  genres: string[];
  predictedRating: number;
  whyThis: string;
  isReleased: boolean;
  providers?: { name: string; logoPath: string | null }[];
}

export interface FeedRow {
  kind: string;
  anchorTitleId: string | null;
  anchorName: string | null;
  title: string | null;
  items: FeedCard[];
}

export interface FeedPayload {
  generatedAt: string | null;
  hero: FeedCard[];
  rows: FeedRow[];
  lockedRowCount: number;
}

export interface ContinueEntry {
  titleId: string;
  mediaType: "Movie" | "Show";
  tmdbId: number | null;
  name: string;
  posterPath: string | null;
  backdropPath: string | null;
  nextEpisodeSeason: number | null;
  nextEpisodeNumber: number | null;
  watchedEpisodes: number;
  totalAired: number;
  completionPct: number;
}
