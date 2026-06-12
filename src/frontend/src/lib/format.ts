/** Shared display formatting (kept pure for unit tests). */

export function formatRuntime(minutes: number | null | undefined): string | null {
  if (!minutes || minutes <= 0) return null;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  if (h === 0) return `${m}m`;
  return m === 0 ? `${h}h` : `${h}h ${m}m`;
}

export function formatEpisodeRef(season: number | null, episode: number | null): string | null {
  if (season == null || episode == null) return null;
  return `S${season} E${episode}`;
}
