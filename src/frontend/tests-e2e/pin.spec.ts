import { test, expect } from "@playwright/test";
import { SESSION_FOUNDER, mockJson, stubEventSource } from "./helpers";

const FILTERS = {
  excludeGenres: [], includeGenres: [], excludeKeywords: [], maturityCeiling: null, minPredictedRating: null,
};

/**
 * The settings PIN keeps content preferences out of reach on shared screens (kids' profiles).
 * Locked by default for a PIN-configured account until the right PIN is entered; a wrong PIN
 * is rejected and the controls stay hidden.
 */
test.describe("settings PIN", () => {
  async function boot(page: import("@playwright/test").Page) {
    await stubEventSource(page);
    await mockJson(page, "**/api/auth/me", { ...SESSION_FOUNDER, pinConfigured: true });
    await mockJson(page, "**/api/sync/status", {
      pipelineStage: "FeedReady", connectionStatus: "Active", lastDeltaSyncAt: null,
      lastFullReconcileAt: null, activeJob: null, recentJobs: [], outboxPending: 0, outboxDeadLetters: 0,
    });
    await mockJson(page, "**/api/settings/filters", FILTERS);
    await page.goto("/settings");
  }

  test("a PIN-configured account is locked until the correct PIN unlocks it", async ({ page }) => {
    await boot(page);
    // Wrong PIN → 423 Locked; correct PIN → 200 {valid:true}.
    await page.route("**/api/settings/pin/verify", async (route) => {
      const body = route.request().postDataJSON() as { pin: string };
      if (body.pin === "2468") {
        await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ valid: true }) });
      } else {
        await route.fulfill({ status: 423, contentType: "application/json", body: "{}" });
      }
    });

    await expect(page.getByTestId("prefs-locked")).toBeVisible();
    await expect(page.getByTestId("prefs-section")).toHaveCount(0);

    // Wrong PIN stays locked with feedback.
    await page.getByTestId("pin-input").fill("9999");
    await page.getByRole("button", { name: "Unlock" }).click();
    await expect(page.getByText(/wrong pin/i)).toBeVisible();
    await expect(page.getByTestId("prefs-locked")).toBeVisible();

    // Correct PIN reveals the controls.
    await page.getByTestId("pin-input").fill("2468");
    await page.getByRole("button", { name: "Unlock" }).click();
    await expect(page.getByTestId("prefs-section")).toBeVisible();
    await expect(page.getByTestId("min-rating-slider")).toBeVisible();
  });
});
