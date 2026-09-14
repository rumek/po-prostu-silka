/**
 * SEED - the exemplar every generated E2E spec is modelled on. What it shows is what you get:
 * role-based locators, waiting for state rather than time, a name that states the risk, and a test
 * that stands alone. Change it deliberately.
 *
 * Authenticated via the storageState the `setup` project saved - no UI login here.
 *
 * A spec that CREATES data must also: suffix names with Date.now() so parallel runs and re-runs never
 * collide, and delete what it created (through the `request` fixture) in the same test or afterEach.
 * This one only reads, so it has nothing to clean up.
 */
import { expect, test } from '@playwright/test';
import { adminDisplayName } from './credentials';

test.describe('session', () => {
  test('a signed-in session survives a full page reload', async ({ page }) => {
    // Open the dashboard with the saved session cookie.
    await page.goto('/');
    await expect(
      page.getByRole('heading', { level: 1, name: `Cześć, ${adminDisplayName}` }),
    ).toBeVisible();

    // Reload - the SPA must re-resolve the session from the cookie, not bounce to /login.
    await page.reload();
    await expect(
      page.getByRole('heading', { level: 1, name: `Cześć, ${adminDisplayName}` }),
    ).toBeVisible();
    await expect(page).toHaveURL('/');
  });
});
