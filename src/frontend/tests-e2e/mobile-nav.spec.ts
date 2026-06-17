import { test, expect } from "@playwright/test";
import { SESSION_FOUNDER, mockJson, stubEventSource } from "./helpers";

const FEED = {
  generatedAt: new Date().toISOString(),
  hero: [
    {
      titleId: "00000000-0000-0000-0000-000000000001",
      mediaType: "Movie",
      tmdbId: 1001,
      name: "Hero Pick",
      year: 2026,
      runtimeMinutes: 110,
      posterPath: null,
      backdropPath: null,
      genres: ["drama"],
      predictedRating: 8.4,
      whyThis: "Strong fit with your taste profile.",
    },
  ],
  rows: [],
  lockedRowCount: 0,
};

/**
 * The media feed wants full-width rows, so phones navigate via a fixed bottom tab bar instead
 * of the desktop inline nav — and search, which is desktop-only in the header, must stay
 * reachable through that bar. This locks both in: the search box was once hidden on mobile
 * with no alternative, stranding Ask Reel.
 */
test.describe("mobile navigation", () => {
  test.use({ viewport: { width: 390, height: 844 } });

  async function bootHome(page: import("@playwright/test").Page) {
    await stubEventSource(page);
    await mockJson(page, "**/api/auth/me", SESSION_FOUNDER);
    await mockJson(page, "**/api/sync/status", {
      pipelineStage: "FeedReady", connectionStatus: "Active", lastDeltaSyncAt: null,
      lastFullReconcileAt: null, activeJob: null, recentJobs: [], outboxPending: 0, outboxDeadLetters: 0,
    });
    await mockJson(page, "**/api/feed/continue-watching", []);
    await mockJson(page, "**/api/feed", FEED);
    await page.goto("/home");
  }

  test("bottom tab bar replaces the hidden inline nav on phones", async ({ page }) => {
    await bootHome(page);

    await expect(page.getByTestId("mobile-tabbar")).toBeVisible();
    // The desktop inline nav is hidden at this width.
    await expect(page.locator("header nav.hidden")).toBeHidden();
  });

  test("the search tab opens a working search panel (Ask Reel reachable)", async ({ page }) => {
    await bootHome(page);
    await mockJson(page, "**/api/search/typeahead**", {
      titles: [{
        titleId: "00000000-0000-0000-0000-0000000000aa",
        mediaType: "Movie", tmdbId: 2002, name: "Medieval Quest", year: 2019,
        posterPath: null, isFullyWatched: false, predictedRating: 7.5,
      }],
      people: [],
    });

    // Search lives behind a tab, not the header, on mobile.
    const panel = page.getByTestId("mobile-search-panel");
    await expect(panel).toBeHidden();
    await page.getByTestId("mobile-search-tab").click();
    await expect(panel).toBeVisible();

    await panel.getByTestId("search-input").fill("medieval");
    await expect(panel.getByTestId("typeahead-result")).toBeVisible();

    await panel.getByTestId("ask-reel-row").click();
    await expect(page).toHaveURL(/\/search\?q=medieval/);
    // Navigating closes the panel.
    await expect(panel).toBeHidden();
  });
});
