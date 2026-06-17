import { describe, expect, it } from "vitest";
import { logoUrl, posterUrl, profileUrl } from "./tmdbImages";

describe("tmdbImages", () => {
  it("logoUrl builds a provider logo URL on the TMDB ladder", () => {
    expect(logoUrl("/abc.png")).toBe("https://image.tmdb.org/t/p/w92/abc.png");
    expect(logoUrl("/abc.png", "w45")).toBe("https://image.tmdb.org/t/p/w45/abc.png");
  });

  it("image helpers return null for missing paths", () => {
    expect(logoUrl(null)).toBeNull();
    expect(logoUrl(undefined)).toBeNull();
    expect(posterUrl(null)).toBeNull();
    expect(profileUrl("")).toBeNull();
  });
});
