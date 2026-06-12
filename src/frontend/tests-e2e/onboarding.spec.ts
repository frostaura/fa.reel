import { test, expect } from "@playwright/test";
import { EMPTY_SYNC, SESSION_FOUNDER, emit, mockJson, stubEventSource, waitForStream } from "./helpers";

/**
 * The live build-up show against a fully mocked backend + scripted SSE — pins the staged
 * advance, counter rendering, and the poll-overrides-stalled-stream contract.
 */
test.describe("onboarding build-up", () => {
  test("stages advance through the scripted pipeline to the reveal", async ({ page }) => {
    await stubEventSource(page);
    await mockJson(page, "**/api/auth/me", { ...SESSION_FOUNDER, onboarded: false, pipelineStage: "Ingesting" });
    await mockJson(page, "**/api/sync/status", EMPTY_SYNC);
    await mockJson(page, "**/api/feed", { generatedAt: null, hero: [], rows: [], lockedRowCount: 0 });

    await page.goto("/onboarding");
    // The mount-time status poll (authoritative) may already advance past "connecting".
    await waitForStream(page);

    await emit(page, "connected", { traktUser: "test-founder" });
    await emit(page, "ingest-progress", { kind: "movies", found: 2842 });
    await expect(page.getByTestId("stage-copy")).toContainText("Pulling in everything");
    await expect(page.getByTestId("tickers")).toContainText("2,842");

    await emit(page, "ingest-progress", { kind: "ratings", found: 3484 });
    await emit(page, "insight", { id: "i1", kind: "genre", text: "You rate crime and thriller highest" });
    await expect(page.getByTestId("stage-copy")).toContainText("Reading your taste");
    await expect(page.getByTestId("insights")).toContainText("crime and thriller");

    await emit(page, "model-progress", { phase: "Teaching your model", processed: 1400, total: 1782 });
    await expect(page.getByTestId("stage-copy")).toContainText("Teaching your model");

    await emit(page, "feed-ready", {});
    await expect(page.getByTestId("reveal")).toBeVisible();
    await expect(page.getByTestId("enter-app")).toBeVisible();
  });

  test("a late ingest event never regresses the stage", async ({ page }) => {
    await stubEventSource(page);
    await mockJson(page, "**/api/auth/me", { ...SESSION_FOUNDER, onboarded: false, pipelineStage: "Ingesting" });
    await mockJson(page, "**/api/sync/status", EMPTY_SYNC);

    await page.goto("/onboarding");
    await waitForStream(page);
    await emit(page, "insight", { id: "i1", kind: "stat", text: "Profiling started" });
    await expect(page.getByTestId("stage-copy")).toContainText("Reading your taste");

    // Out-of-order ingest frame: counters update, stage must hold.
    await emit(page, "ingest-progress", { kind: "shows", found: 249 });
    await expect(page.getByTestId("tickers")).toContainText("249");
    await expect(page.getByTestId("stage-copy")).toContainText("Reading your taste");
  });

  test("the authoritative status poll advances a silent stream", async ({ page }) => {
    await stubEventSource(page);
    await mockJson(page, "**/api/auth/me", { ...SESSION_FOUNDER, onboarded: false, pipelineStage: "Training" });
    await mockJson(page, "**/api/sync/status", { ...EMPTY_SYNC, pipelineStage: "Training" });

    await page.goto("/onboarding");
    // No SSE events at all — the first poll lands within its interval and advances the show.
    await expect(page.getByTestId("stage-copy")).toContainText("Teaching your model", { timeout: 20_000 });
  });
});
