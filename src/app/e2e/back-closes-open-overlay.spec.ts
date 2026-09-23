/**
 * Risk: on a phone, the system back gesture left the schedule instead of closing the bookings
 * overlay open on top of it — the first thing an Android user tries to dismiss a dialog with
 * (mobile-native-feel, Phase 2).
 * Seed: seed.spec.ts. Runs as the seeded admin at a phone width, where the admin's Grafik is
 * /schedule and a class opens its bookings overlay.
 *
 * Creates a class type (unique name) and one class of it through the API; afterEach deletes the
 * class and deactivates the type — types cannot be deleted.
 */
import { APIRequestContext, expect, test } from '@playwright/test';

test.use({ viewport: { width: 390, height: 844 } });

let classId: string | null = null;
let classTypeId: string | null = null;

/**
 * A start inside the calendar's visible hours (06:00–23:00) that is still in the future: an hour
 * from now while that fits, otherwise tomorrow morning.
 */
function nextStart(): Date {
  const start = new Date(Date.now() + 60 * 60 * 1000);
  start.setMinutes(Math.ceil(start.getMinutes() / 15) * 15, 0, 0);

  if (start.getHours() >= 6 && start.getHours() < 22 && start.getDate() === new Date().getDate()) {
    return start;
  }

  const tomorrow = new Date();
  tomorrow.setDate(tomorrow.getDate() + 1);
  tomorrow.setHours(10, 0, 0, 0);
  return tomorrow;
}

async function createClass(request: APIRequestContext, name: string, startsAt: Date) {
  const trainers = await request.get('/api/admin/trainers');
  expect(trainers.ok()).toBeTruthy();
  const [trainer] = (await trainers.json()) as { id: string }[];
  expect(trainer, 'the database needs at least one trainer').toBeDefined();

  const type = await request.post('/api/admin/class-types', {
    data: { name, description: null, defaultDurationMinutes: 30, defaultCapacity: 5 },
  });
  expect(type.ok()).toBeTruthy();
  classTypeId = ((await type.json()) as { id: string }).id;

  const created = await request.post('/api/admin/classes', {
    data: {
      classTypeId,
      startsAt: startsAt.toISOString(),
      durationMinutes: 30,
      instructorMemberId: trainer.id,
      capacity: 5,
    },
  });
  expect(created.ok()).toBeTruthy();
  classId = ((await created.json()) as { id: string }).id;
}

test.afterEach(async ({ request }) => {
  if (classId) {
    await request.delete(`/api/admin/classes/${classId}`);
    classId = null;
  }
  if (classTypeId) {
    await request.post(`/api/admin/class-types/${classTypeId}/deactivate`);
    classTypeId = null;
  }
});

test('back closes the open bookings overlay and stays on the schedule', async ({
  page,
  request,
}) => {
  const name = `E2E powrót ${Date.now()}`;
  const startsAt = nextStart();
  await createClass(request, name, startsAt);

  await page.goto('/');
  await page
    .getByRole('navigation', { name: 'Nawigacja główna' })
    .getByRole('link', { name: 'Grafik' })
    .click();
  await page.waitForURL('/schedule');

  // The phone calendar shows one day; move to the class's day if it is not today.
  if (startsAt.toDateString() !== new Date().toDateString()) {
    const label = startsAt.toLocaleDateString('pl-PL', {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      year: 'numeric',
    });
    await expect(page.getByRole('group', { name: 'Dni tygodnia' })).toBeVisible();
    const day = page.getByRole('button', { name: label });
    if (!(await day.isVisible())) {
      await page.getByRole('button', { name: 'Następny tydzień' }).click();
    }
    await day.click();
  }

  await page.getByRole('button', { name: new RegExp(name) }).click();
  const dialog = page.getByRole('dialog', { name: `Zapisani na „${name}”` });
  await expect(dialog).toBeVisible();

  await page.goBack();

  await expect(dialog).toBeHidden();
  await expect(page).toHaveURL('/schedule');
  await expect(page.getByRole('heading', { name: 'Grafik', exact: true })).toBeVisible();
});
