import { defineConfig, devices } from '@playwright/test';
import { authFile } from './e2e/credentials';

/**
 * Browser-level (E2E) tests. Rules for writing them live in e2e/CLAUDE.md; seed.spec.ts is the
 * exemplar every new spec is modelled on.
 *
 * The app under test is the REAL shape it ships in: the ASP.NET Core API serving the built SPA from
 * its wwwroot, on one origin, against the local Docker SQL Server. `ng serve` is not used - it has no
 * /api proxy, so every call the SPA makes would 404 and nothing past the login form could be driven.
 *
 * Prerequisites: `docker compose up -d` and migrations applied to the local database.
 */
const baseURL = process.env['E2E_BASE_URL'] ?? 'http://localhost:5264';

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 2 : 0,
  reporter: 'list',
  use: {
    baseURL,
    trace: 'on-first-retry',
  },
  projects: [
    // Signs in through the API once and saves the cookie, so specs never log in through the UI.
    { name: 'setup', testMatch: /.*\.setup\.ts/ },
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], storageState: authFile },
      dependencies: ['setup'],
    },
  ],
  webServer: {
    // Rebuilds the SPA into wwwroot first, so the browser never drives a stale bundle.
    command:
      'npm run e2e:stage && dotnet run --project ../po-prostu-silka.csproj --launch-profile http',
    // /health opens a real DB connection - "up" means the API can actually reach SQL Server.
    url: `${baseURL}/health`,
    reuseExistingServer: !process.env['CI'],
    timeout: 240_000,
  },
});
