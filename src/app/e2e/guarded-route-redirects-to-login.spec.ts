/**
 * Risk: an anonymous visitor reaches member content, or a valid sign-in does not actually open the
 * app. Crosses routing (authGuard), the auth API, the HttpOnly cookie and the database behind
 * Identity - no single unit or integration test covers all four together.
 *
 * Seed: seed.spec.ts. Rules: e2e/CLAUDE.md.
 */
import { expect, test } from '@playwright/test';
import { adminCredentials, adminDisplayName } from './credentials';

// This spec tests the login form itself, so it starts WITHOUT the saved session.
test.use({ storageState: { cookies: [], origins: [] } });

test.describe('auth guard', () => {
  test('anonymous visitor on a guarded route is sent to login, and signing in opens the dashboard', async ({
    page,
  }) => {
    // Record every request for member data (anything under /api/ except the auth endpoints the
    // anonymous shell legitimately calls). Landing on /login ALONE proves nothing about the guard:
    // without it the screen still renders, asks the API, gets a 401, and authInterceptor redirects to
    // /login anyway. Only "the screen never asked for member data" tells the guard did its job.
    const memberDataRequests: string[] = [];
    page.on('request', (request) => {
      const path = new URL(request.url()).pathname;
      if (path.startsWith('/api/') && !path.startsWith('/api/auth/')) {
        memberDataRequests.push(path);
      }
    });

    // An anonymous visitor opens a member-only route.
    await page.goto('/my-classes');

    // The guard sends them to the login screen before the route renders or fetches anything.
    await page.waitForURL('**/login');
    await expect(page.getByRole('heading', { level: 1, name: 'Cześć!' })).toBeVisible();
    expect(memberDataRequests).toEqual([]);

    // They sign in with valid credentials.
    await page.getByLabel('Adres e-mail').fill(adminCredentials.email);
    await page.getByLabel('Hasło').fill(adminCredentials.password);
    await page.getByRole('button', { name: 'Zaloguj się' }).click();

    // The dashboard opens for THIS account, and the signed-in shell is shown.
    await page.waitForURL((url) => url.pathname === '/');
    await expect(
      page.getByRole('heading', { level: 1, name: `Cześć, ${adminDisplayName}` }),
    ).toBeVisible();
    await expect(page.getByRole('button', { name: 'Wyloguj się' }).first()).toBeVisible();
  });
});
