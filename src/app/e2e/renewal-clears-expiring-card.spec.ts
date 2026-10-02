/**
 * Risk: a renewal the admin issues on the karnet screen does not take the member off the "Kończą się
 * karnety" card on Start, nor off the member list's "Tylko kończące się" that the card links to — the
 * predicate missing the "a later karnet exists" clause, or the two surfaces reading "ending" two
 * different ways (expiring-passes-dashboard). The write is on one screen and the reads are on two
 * others, behind the admin persona; only a browser crosses all of them.
 * Seed: seed.spec.ts.
 *
 * THE CARD IS CLUB-WIDE: it shows the five nearest ends in the whole local database, so whether this
 * member makes the cut depends on everyone else's karnets. The "before" is therefore asserted on the
 * list, narrowed to this member by name; the "after" on both, where absence is exact.
 *
 * Creates a member `E2E odnowienie <ts>` with a karnet ending in two days, which the `club` fixture
 * revokes, and a renewal issued through the form, which stays behind on the E2E member.
 */
import { uniqueSuffix } from './support/club';
import { expect, test } from './support/fixtures';

/** `yyyy-MM-dd` for a day `offset` from today — what a date input takes. */
function day(offset: number): string {
  const date = new Date();
  date.setDate(date.getDate() + offset);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

test('renewing an ending karnet takes the member off the expiring card and list', async ({
  club,
  page,
}) => {
  // Arrange: a member whose karnet covers today and ends in two days, with nothing after it.
  const name = `E2E odnowienie ${uniqueSuffix()}`;
  const memberId = await club.createMember(name);
  await club.issuePass(memberId, 8, 2, { validFromDaysAgo: 27 });

  // The list the card's "Zobacz wszystkich" opens lists them as ending.
  const expiringList = `/admin/members?q=${encodeURIComponent(name)}&expiring=1`;
  await page.goto(expiringList);
  await expect(page.getByLabel('Tylko kończące się')).toBeChecked();
  await expect(page.getByText(name).first()).toBeVisible();

  // Renew on the member's karnet screen: the next karnet starts the day after this one ends.
  await page.goto(`/admin/members/${memberId}/passes`);
  await expect(page.getByRole('heading', { level: 1, name: `Karnety — ${name}` })).toBeVisible();

  await page.getByLabel('Nazwa karnetu').fill('Karnet E2E odnowiony');
  await page.getByLabel('Ważny od').fill(day(3));
  await page.getByLabel('Ważny do').fill(day(32));
  await page.getByLabel('Liczba wejść').fill('8');

  const issued = page.waitForResponse(
    (response) =>
      response.url().endsWith(`/api/admin/members/${memberId}/passes`) &&
      response.request().method() === 'POST',
  );
  await page.getByRole('button', { name: 'Wystaw karnet' }).click();
  expect((await issued).ok(), 'the admin issued the renewal').toBeTruthy();

  // The list no longer lists them as ending.
  await page.goto(expiringList);
  await expect(page.getByText('Brak członków pasujących do wyszukiwania.')).toBeVisible();
  await expect(page.getByText(name)).toBeHidden();

  // Nor does the card on Start, once it has loaded its rows or its empty state.
  await page.goto('/');
  const card = page.getByRole('region', { name: 'Klub' });
  await expect(card.getByRole('heading', { name: 'Kończą się karnety' })).toBeVisible();
  await expect(card.getByText('Wczytywanie')).toBeHidden();
  await expect(card.getByText(name)).toBeHidden();
});
