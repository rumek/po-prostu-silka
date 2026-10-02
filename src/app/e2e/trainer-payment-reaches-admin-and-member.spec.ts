/**
 * Risk: a payment a trainer records is not the payment the admin and the member read - the trainer's
 * route refused by a policy that drifted from its guard (S-25), the admin's "Tylko nieopłacone" still
 * listing the member because the list reads payment differently from the karnet screens, or the
 * member's Start card built from a third view that never learned about payment (PASS-12, PASS-14,
 * DASH-01). Three personas and three read paths over one write: only a browser crosses all of them.
 * Seed: seed.spec.ts. The karnet is issued through `club.issuePass`, which posts the four original
 * fields only - so it is unpaid, the issue form's default.
 *
 * Creates a member `E2E płatność <ts>` with an `@example.test` account and one karnet. The `club`
 * fixture revokes the karnet; the member and the account stay behind.
 */
import { memberPassword, trainerCredentials } from './credentials';
import { uniqueSuffix } from './support/club';
import { expect, test } from './support/fixtures';
import { signedInContext } from './support/sessions';

test('a payment a trainer records clears the admin unpaid filter and reaches the member', async ({
  browser,
  club,
  page,
}) => {
  // Arrange: a member with an account and an unpaid karnet covering today.
  const suffix = uniqueSuffix();
  const name = `E2E płatność ${suffix}`;
  const email = `e2e-platnosc-${suffix}@example.test`;

  const memberId = await club.createMember(name);
  await club.registerMember(memberId, email, memberPassword);
  await club.issuePass(memberId, 8);

  // Admin (the default session): "Tylko nieopłacone" lists the member before anyone has paid.
  const unpaidList = `/admin/members?q=${encodeURIComponent(name)}&unpaid=1`;
  await page.goto(unpaidList);
  await expect(page.getByText(name)).toBeVisible();

  const trainer = await signedInContext(browser, trainerCredentials);
  try {
    const trainerPage = await trainer.newPage();

    // Trainer: the member's row says Nieopłacony, and Karnety opens their karnets.
    await trainerPage.goto('/trainer/members');
    await trainerPage.getByRole('searchbox', { name: 'Szukaj członka' }).fill(name);
    const row = trainerPage.getByRole('listitem').filter({ hasText: name });
    await expect(row.getByText('Nieopłacony')).toBeVisible();
    await row.getByRole('link', { name: 'Karnety' }).click();
    await expect(
      trainerPage.getByRole('heading', { level: 1, name: `Karnety — ${name}` }),
    ).toBeVisible();

    // Mark it paid through the overlay, on today's default day. Waits on the write itself rather
    // than on its toast, whose words are on the page twice (see trainer-plan-reaches-member).
    await trainerPage.getByRole('button', { name: 'Oznacz jako opłacony' }).click();
    const dialog = trainerPage.getByRole('dialog', { name: 'Oznacz jako opłacony' });
    await expect(dialog.getByLabel('Data płatności')).not.toHaveValue('');

    const saved = trainerPage.waitForResponse(
      (response) =>
        /\/api\/passes\/[^/]+\/paid$/.test(response.url()) && response.request().method() === 'PUT',
    );
    await dialog.getByRole('button', { name: 'Zapisz' }).click();
    expect((await saved).ok(), 'the trainer recorded the payment').toBeTruthy();

    await expect(dialog).toBeHidden();
    await expect(trainerPage.getByRole('button', { name: 'Cofnij płatność' })).toBeVisible();
  } finally {
    await trainer.close();
  }

  // Admin: the same filter no longer lists the member.
  await page.goto(unpaidList);
  await expect(page.getByText('Brak członków pasujących do wyszukiwania.')).toBeVisible();
  await expect(page.getByText(name)).toBeHidden();

  // Member: Start says the karnet is paid.
  const member = await signedInContext(browser, { email, password: memberPassword });
  try {
    const memberPage = await member.newPage();
    await memberPage.goto('/');

    const card = memberPage
      .getByRole('article')
      .filter({ has: memberPage.getByText('Pozostałe wejścia') });
    await expect(card.getByText('Opłacony', { exact: true })).toBeVisible();
    await expect(card.getByText('Nieopłacony')).toBeHidden();
  } finally {
    await member.close();
  }
});
