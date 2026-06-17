/**
 * View-level browse filters — transient UI state (not persisted ContentFilters). Media type
 * and release-availability buckets applied at read time to already-fetched cards.
 *
 * Note on "available": true streaming-availability is per-title and sparse (see ProviderEndpoints
 * 24h cache), so it can't filter the whole feed honestly. "Available now" here means *released*
 * (out, findable); "Coming soon" means not yet released. The where-to-watch panel on each title
 * shows the real streaming options.
 */
export type MediaFilter = "all" | "movie" | "show";
export type AvailabilityFilter = "all" | "available" | "coming";

export interface BrowseFilter {
  media: MediaFilter;
  availability: AvailabilityFilter;
}

export const DEFAULT_BROWSE_FILTER: BrowseFilter = { media: "all", availability: "all" };

export interface BrowseCard {
  mediaType: "Movie" | "Show";
  isReleased: boolean;
}

/** True when the card passes the active filter. */
export function matchesBrowseFilter(card: BrowseCard, f: BrowseFilter): boolean {
  if (f.media === "movie" && card.mediaType !== "Movie") return false;
  if (f.media === "show" && card.mediaType !== "Show") return false;
  if (f.availability === "available" && !card.isReleased) return false;
  if (f.availability === "coming" && card.isReleased) return false;
  return true;
}

/** Media-only predicate — for surfaces without release data (e.g. continue-watching). */
export function matchesMedia(mediaType: "Movie" | "Show", f: BrowseFilter): boolean {
  return f.media === "all" || (f.media === "movie" ? mediaType === "Movie" : mediaType === "Show");
}

export const isFilterActive = (f: BrowseFilter): boolean =>
  f.media !== "all" || f.availability !== "all";
