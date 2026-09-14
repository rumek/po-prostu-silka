import { expect, test as setup } from '@playwright/test';
import { adminCredentials, authFile } from './credentials';

/**
 * Authenticates once, through the API rather than the form, and saves the HttpOnly auth cookie for
 * every spec in the `chromium` project. The login FORM itself is exercised by exactly one spec
 * (guarded-route-redirects-to-login.spec.ts), which opts out of this state.
 */
setup('authenticate as the seeded admin', async ({ request }) => {
  const response = await request.post('/api/auth/login', { data: adminCredentials });
  expect(response.ok()).toBeTruthy();

  await request.storageState({ path: authFile });
});
