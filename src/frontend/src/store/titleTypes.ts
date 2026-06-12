/** Title detail payload (mirrors TitleEndpoints). */
export interface CastMember {
  personId: string;
  name: string;
  role: string;
  character: string | null;
  castOrder: number | null;
  profilePath: string | null;
}

export interface TitleDetailPayload {
  titleId: string;
  mediaType: "Movie" | "Show";
  tmdbId: number | null;
  name: string;
  year: number | null;
  overview: string | null;
  tagline: string | null;
  runtimeMinutes: number | null;
  certification: string | null;
  genres: string[];
  network: string | null;
  status: string | null;
  traktRating: number | null;
  traktVotes: number;
  posterPath: string | null;
  backdropPath: string | null;
  trailerUrl: string | null;
  cast: CastMember[];
  directors: { personId: string; name: string }[];
  writers: { personId: string; name: string }[];
  prediction: {
    predictedRating: number;
    contributions: { feature: string; value: number }[];
    scoredAt: string;
  } | null;
  userState: {
    isFullyWatched: boolean;
    plays: number;
    userRating: number | null;
    savedForLater: boolean;
    notInterested: boolean;
  };
}

export interface SavedEntry {
  titleId: string;
  mediaType: "Movie" | "Show";
  tmdbId: number | null;
  name: string;
  year: number | null;
  posterPath: string | null;
  predictedRating: number | null;
  savedAt: string;
  onManagedList: boolean;
}
