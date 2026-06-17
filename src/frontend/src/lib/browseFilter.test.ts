import { describe, expect, it } from "vitest";
import { matchesBrowseFilter, matchesMedia, isFilterActive, DEFAULT_BROWSE_FILTER } from "./browseFilter";

const movie = { mediaType: "Movie" as const, isReleased: true };
const showComing = { mediaType: "Show" as const, isReleased: false };

describe("browseFilter", () => {
  it("default passes everything", () => {
    expect(matchesBrowseFilter(movie, DEFAULT_BROWSE_FILTER)).toBe(true);
    expect(matchesBrowseFilter(showComing, DEFAULT_BROWSE_FILTER)).toBe(true);
    expect(isFilterActive(DEFAULT_BROWSE_FILTER)).toBe(false);
  });

  it("media filter keeps only the chosen type", () => {
    expect(matchesBrowseFilter(movie, { media: "movie", availability: "all" })).toBe(true);
    expect(matchesBrowseFilter(showComing, { media: "movie", availability: "all" })).toBe(false);
    expect(matchesBrowseFilter(showComing, { media: "show", availability: "all" })).toBe(true);
  });

  it("availability buckets released vs coming-soon", () => {
    expect(matchesBrowseFilter(movie, { media: "all", availability: "available" })).toBe(true);
    expect(matchesBrowseFilter(movie, { media: "all", availability: "coming" })).toBe(false);
    expect(matchesBrowseFilter(showComing, { media: "all", availability: "coming" })).toBe(true);
    expect(matchesBrowseFilter(showComing, { media: "all", availability: "available" })).toBe(false);
  });

  it("combines media + availability", () => {
    expect(matchesBrowseFilter(movie, { media: "movie", availability: "available" })).toBe(true);
    expect(matchesBrowseFilter(showComing, { media: "movie", availability: "coming" })).toBe(false);
  });

  it("matchesMedia ignores release status (for continue-watching)", () => {
    expect(matchesMedia("Show", { media: "show", availability: "available" })).toBe(true);
    expect(matchesMedia("Movie", { media: "show", availability: "all" })).toBe(false);
  });

  it("isFilterActive flags any non-default", () => {
    expect(isFilterActive({ media: "movie", availability: "all" })).toBe(true);
    expect(isFilterActive({ media: "all", availability: "coming" })).toBe(true);
  });
});
