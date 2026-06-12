import { test, expect } from "@playwright/test";

/**
 * Frontend-only shell smoke — the CI gate. No backend: /api calls are mocked at the route
 * layer so the landing page renders deterministically.
 */
test.describe("shell smoke", () => {
  test("landing renders brand, sign-in CTA and TMDB attribution", async ({ page }) => {
    // Signed-out session: auth/me 401s.
    await page.route("**/api/auth/me", (route) =>
      route.fulfill({ status: 401, contentType: "application/json", body: "{}" })
    );

    await page.goto("/");

    await expect(page).toHaveTitle(/Reel/);
    await expect(page.locator("#root")).toBeVisible();
    await expect(page.getByRole("button", { name: /sign in with trakt/i })).toBeVisible();
    await expect(page.getByText(/uses the TMDB API/i)).toBeVisible();
    await expect(page.getByText(/Reel by/i)).toBeVisible();
  });
});
