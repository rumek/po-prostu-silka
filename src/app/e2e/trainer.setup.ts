import { expect, test as setup } from '@playwright/test';
import {
  adminCredentials,
  trainerAuthFile,
  trainerCredentials,
  trainerDisplayName,
} from './credentials';
import { Cleanup } from './support/cleanup';
import { Club } from './support/club';

/**
 * Ensures the ONE E2E trainer exists and saves its session - once per run, before any spec, on any
 * database, including one holding only the seeded admin (a fresh CI database). Classes the specs
 * create are instructed by this account.
 *
 * It repairs whichever step is missing - record, account, Trainer role - so a run that died halfway
 * through creating it heals on the next one. On a database where it exists, it only signs in.
 *
 * It signs in as the admin ITSELF rather than reading admin.json: setup files run in parallel, so
 * auth.setup.ts may not have written that file yet.
 */
setup('ensure the E2E trainer exists', async ({ playwright, baseURL }) => {
  const admin = await playwright.request.newContext({ baseURL });
  const signIn = await admin.post('/api/auth/login', { data: adminCredentials });
  expect(signIn.ok()).toBeTruthy();

  // Nothing here is ever removed: the trainer is meant to outlive the run.
  const club = new Club(admin, new Cleanup(), baseURL!);

  let trainer =
    (await club.findMember(trainerCredentials.email)) ??
    (await club.findMember(trainerDisplayName));

  if (!trainer) {
    await club.createMember(trainerDisplayName);
    trainer = await club.findMember(trainerDisplayName);
  }
  expect(trainer, 'the E2E trainer record').toBeDefined();

  if (!trainer!.userId) {
    await club.registerMember(trainer!.id, trainerCredentials.email, trainerCredentials.password);
  }

  if (!trainer!.roles.includes('Trainer')) {
    const grant = await admin.post(`/api/admin/members/${trainer!.id}/roles/trainer`);
    expect(grant.ok(), `grant Trainer: ${await grant.text()}`).toBeTruthy();
  }

  await admin.dispose();

  // Signed in AFTER the grant, so the session carries the Trainer role.
  const session = await playwright.request.newContext({ baseURL });
  const trainerSignIn = await session.post('/api/auth/login', { data: trainerCredentials });
  expect(
    trainerSignIn.ok(),
    `sign-in as ${trainerCredentials.email} - if the account exists with another password, reset it`,
  ).toBeTruthy();
  await session.storageState({ path: trainerAuthFile });
  await session.dispose();
});
