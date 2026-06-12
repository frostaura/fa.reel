import { test, expect } from "@playwright/test";
import { SESSION_FOUNDER, mockJson, stubEventSource } from "./helpers";

const card = (n: number) => ({
  titleId: `00000000-0000-0000-0000-00000000000${n}`,
  mediaType: "Movie",
  tmdbId: 1000 + n,
  name: `Pick ${n}`,
  year: 2026,
  runtimeMinutes: 110,
  posterPath: null,
  backdropPath: null,
  genres: ["drama"],
  predictedRating: 8.1,
  whyThis: "Strong fit with your taste profile.",
});

test.describe("entitlement gating", () => {
  test("free tier sees the 3-pick shortlist and the locked-rows upsell", async ({ page }) => {
    await stubEventSource(page);
    await mockJson(page, "**/api/auth/me", { ...SESSION_FOUNDER, tier: "Free" });
    await mockJson(page, "**/api/sync/status", {
      pipelineStage: "FeedReady", connectionStatus: "Active", lastDeltaSyncAt: null,
      lastFullReconcileAt: null, activeJob: null, recentJobs: [], outboxPending: 0, outboxDeadLetters: 0,
    });
    await mockJson(page, "**/api/feed/continue-watching", []);
    // Server-side strip: free payload carries only 3 hero cards + a locked-row count.
    await mockJson(page, "**/api/feed", {
      generatedAt: new Date().toISOString(),
      hero: [card(1), card(2), card(3)],
      rows: [],
      lockedRowCount: 3,
    });

    await page.goto("/home");

    await expect(page.getByTestId("hero-card")).toHaveCount(3);
    await expect(page.getByText(/more personalised rows on the full feed/i)).toBeVisible();
    await expect(page.getByText(/paid feed unlocks every row/i)).toBeVisible();
  });

  test("founder tier renders rows without the upsell", async ({ page }) => {
    await stubEventSource(page);
    await mockJson(page, "**/api/auth/me", SESSION_FOUNDER);
    await mockJson(page, "**/api/sync/status", {
      pipelineStage: "FeedReady", connectionStatus: "Active", lastDeltaSyncAt: null,
      lastFullReconcileAt: null, activeJob: null, recentJobs: [], outboxPending: 0, outboxDeadLetters: 0,
    });
    await mockJson(page, "**/api/feed/continue-watching", []);
    await mockJson(page, "**/api/feed", {
      generatedAt: new Date().toISOString(),
      hero: [card(1), card(2), card(3), card(4), card(5)],
      rows: [
        {
          kind: "because-you-loved",
          anchorTitleId: "00000000-0000-0000-0000-0000000000aa",
          anchorName: "Anchor Film",
          items: [card(6), card(7), card(8), card(9)],
        },
      ],
      lockedRowCount: 0,
    });

    await page.goto("/home");

    await expect(page.getByTestId("hero-card")).toHaveCount(5);
    await expect(page.getByText("Anchor Film")).toBeVisible();
    await expect(page.getByText(/more personalised rows/i)).toHaveCount(0);
  });
});
