/**
 * TMDB image URL builders. Paths come from the API as "/abc123.jpg"; sizes are TMDB's fixed
 * ladder. The service worker caches image.tmdb.org CacheFirst, so repeat paints are instant.
 */
const BASE = "https://image.tmdb.org/t/p/";

export type PosterSize = "w185" | "w342" | "w500";
export type BackdropSize = "w780" | "w1280";

export function posterUrl(path: string | null | undefined, size: PosterSize = "w342"): string | null {
  return path ? `${BASE}${size}${path}` : null;
}

export function backdropUrl(path: string | null | undefined, size: BackdropSize = "w780"): string | null {
  return path ? `${BASE}${size}${path}` : null;
}

export function profileUrl(path: string | null | undefined): string | null {
  return path ? `${BASE}w185${path}` : null;
}

/** Streaming-provider logo (where-to-watch chips). TMDB serves square logos on the same ladder. */
export function logoUrl(path: string | null | undefined, size: "w45" | "w92" = "w92"): string | null {
  return path ? `${BASE}${size}${path}` : null;
}

/** DPR-aware srcset for poster slots. */
export function posterSrcSet(path: string | null | undefined): string | undefined {
  if (!path) return undefined;
  return `${BASE}w185${path} 185w, ${BASE}w342${path} 342w, ${BASE}w500${path} 500w`;
}
