/** A clickable, rateable person chip — cast or crew. */
export interface CreditPerson {
  personId: string;
  name: string;
  profilePath: string | null;
  userRating: number | null;
  character?: string | null;
}

/** Title detail payload (mirrors TitleEndpoints). */
export interface CastMember extends CreditPerson {
  role: string;
  castOrder: number | null;
}

/** Actor page payload (mirrors PersonEndpoints GET). */
export interface PersonPayload {
  id: string;
  name: string;
  department: string | null;
  profilePath: string | null;
  userRating: number | null;
  derivedAffinity: number | null;
  ratedTitleCount: number;
  filmography: {
    titleId: string;
    mediaType: "Movie" | "Show";
    tmdbId: number | null;
    name: string;
    year: number | null;
    posterPath: string | null;
    userRating: number | null;
    predictedRating: number | null;
  }[];
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
  directors: CreditPerson[];
  writers: CreditPerson[];
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
    dropped: boolean;
  };
}

/** Where-to-watch payload (mirrors ProviderEndpoints). */
export interface ProviderEntry {
  provider: string;
  logoPath: string | null;
  kinds: string[];
  linkKind: "direct" | "tmdb";
  url: string;
  displayPriority: number;
}

export interface ProvidersPayload {
  region: string;
  attribution: string;
  tmdbWatchPage: string;
  providers: ProviderEntry[];
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
