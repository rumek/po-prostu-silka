/**
 * Risk: on a phone, the system back gesture left the schedule instead of closing the bookings
 * overlay open on top of it — the first thing an Android user tries to dismiss a dialog with
 * (mobile-native-feel, Phase 2).
 * Seed: seed.spec.ts. Runs as the seeded admin at a phone width, where the admin's Grafik is
 * /schedule and a class opens its bookings overlay.
 *
 * Creates a class type (unique name) and one class of it, instructed by the E2E trainer, in a random
 * free slot (support/slots.ts); the `club` fixture deletes the class and deactivates the type.
 */
import { uniqueSuffix } from './support/club';
import { expect, test } from './support/fixtures';
import { showWeekOf } from './support/slots';

// The browser renders dates in the SAME timezone and locale this process computes them in, so the
// day label the spec builds is the one the week strip shows.
test.use({
  viewport: { width: 390, height: 844 },
  timezoneId: Intl.DateTimeFormat().resolvedOptions().timeZone,
  locale: 'pl-PL',
});

test('back closes the open bookings overlay and stays on the schedule', async ({ page, club }) => {
  const created = await club.createClass(`E2E powrót ${uniqueSuffix()}`);

  await page.goto('/');
  await page
    .getByRole('navigation', { name: 'Nawigacja główna' })
    .getByRole('link', { name: 'Grafik' })
    .click();
  await page.waitForURL('/schedule');

  // The phone calendar shows one day of a week strip; move to the class's week, then its day.
  await expect(page.getByRole('group', { name: 'Dni tygodnia' })).toBeVisible();
  await showWeekOf(page, created.startsAt);
  const label = created.startsAt.toLocaleDateString('pl-PL', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  });
  await page.getByRole('button', { name: label }).click();

  await page.getByRole('button', { name: new RegExp(created.name) }).click();
  const dialog = page.getByRole('dialog', { name: `Zapisani na „${created.name}”` });
  await expect(dialog).toBeVisible();

  await page.goBack();

  await expect(dialog).toBeHidden();
  await expect(page).toHaveURL('/schedule');
  await expect(page.getByRole('heading', { name: 'Grafik', exact: true })).toBeVisible();
});
