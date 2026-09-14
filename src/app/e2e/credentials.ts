/**
 * The admin AdminSeeder creates from src/appsettings.Development.json. Those values are
 * development-only and deliberately committed; any other environment supplies its own via env vars.
 */
export const adminCredentials = {
  email: process.env['E2E_ADMIN_EMAIL'] ?? 'admin@poprostusilka.local',
  password: process.env['E2E_ADMIN_PASSWORD'] ?? 'LocalAdmin_Pass123',
};

/** Where the `setup` project saves the admin's session; gitignored. */
export const authFile = 'playwright/.auth/admin.json';

/** The seeded admin's DisplayName, which the dashboard greets by. */
export const adminDisplayName = 'Administrator';
