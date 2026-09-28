/**
 * The `test` every spec imports instead of @playwright/test's. It adds:
 *
 * - `adminApi` - an APIRequestContext on the admin's saved session, for arranging data;
 * - `club` - the builders (support/club.ts), each registering its own removal;
 *
 * and tears the removals down after each test, in reverse order, pass or fail.
 */
import { APIRequestContext, test as base } from '@playwright/test';
import { authFile } from '../credentials';
import { Cleanup } from './cleanup';
import { Club } from './club';

interface ClubFixtures {
  adminApi: APIRequestContext;
  club: Club;
}

export const test = base.extend<ClubFixtures>({
  adminApi: async ({ playwright, baseURL }, use) => {
    const api = await playwright.request.newContext({ baseURL, storageState: authFile });
    await use(api);
    await api.dispose();
  },

  club: async ({ adminApi, baseURL }, use) => {
    const cleanup = new Cleanup();
    await use(new Club(adminApi, cleanup, baseURL!));
    await cleanup.runAll();
  },
});

export { expect } from '@playwright/test';
