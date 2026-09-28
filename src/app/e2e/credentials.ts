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

/**
 * The one E2E trainer, get-or-created by trainer.setup.ts on any database - including one holding only
 * the seeded admin. Classes the specs create are instructed by this account. Development-only values,
 * like the admin's.
 */
export const trainerCredentials = {
  email: process.env['E2E_TRAINER_EMAIL'] ?? 'e2e-trainer@example.test',
  password: process.env['E2E_TRAINER_PASSWORD'] ?? 'E2eTrainer_Pass123',
};

/**
 * The E2E trainer's DisplayName. Specs act as the trainer through
 * `signedInContext(browser, trainerCredentials)`; there is no saved trainer session.
 */
export const trainerDisplayName = 'E2E Trener';

/** The password every member a spec registers is given. Development-only. */
export const memberPassword = 'E2eMember_Pass123';
