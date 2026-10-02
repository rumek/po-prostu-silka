/**
 * Risk: an "odrobi" absence a trainer records does not keep its entry spent, or the makeup the club
 * owes for it costs a second entry, or never reaches the member (S-36 class-makeups; MAKEUP-01,
 * ATT-01, ATT-02; test-plan risk #6: entries derived from active bookings). Seed: seed.spec.ts. The
 * missed class is booked in the future and then moved into the past week (club.startClass); the
 * makeup class is another class ahead, inside the thirty-day deadline. The trainer works at a phone
 * width (support/schedule.ts); the member reads Start and Moje zajęcia in their own session.
 *
 * Creates a member `E2E odrabianie <ts>` with an `@example.test` account and a 5-entry karnet valid
 * from a week ago, a started class and a future class, both instructed by the E2E trainer. The
 * started class stays behind as attendance history, as do the member, the account and the karnet;
 * the future class is cancelled, and the makeup item is closed so the club's list does not grow.
 * Both groups are deactivated.
 */
import { memberPassword, trainerCredentials } from './credentials';
import { uniqueSuffix } from './support/club';
import { expect, test } from './support/fixtures';
import { openClassBookings, PHONE } from './support/schedule';
import { signedInContext } from './support/sessions';

test.use(PHONE);

test('an absence marked odrobi keeps its entry spent, and the makeup booked for it costs none', async ({
  browser,
  club,
}) => {
  // Arrange: karnet first (see CLAUDE.md), covering the past week and the weeks ahead.
  const suffix = uniqueSuffix();
  const name = `E2E odrabianie ${suffix}`;
  const email = `e2e-odrabianie-${suffix}@example.test`;
  const memberId = await club.createMember(name);
  await club.registerMember(memberId, email, memberPassword);
  await club.issuePass(memberId, 5, 30, { validFromDaysAgo: 7 });
  const missed = await club.createClass(`E2E klasa opuszczona ${suffix}`);
  await club.book(missed.id, memberId);
  const started = await club.startClass(missed);
  // Before the makeup class exists, so cleanup cancels that class first and the item can close.
  club.closeMakeupAfterwards(started.id, memberId);
  const makeupClass = await club.createClass(`E2E klasa odrabiania ${suffix}`);

  const member = await signedInContext(browser, { email, password: memberPassword });
  const trainer = await signedInContext(browser, trainerCredentials);
  try {
    const memberPage = await member.newPage();
    const page = await trainer.newPage();

    // Trainer: mark the member "Odrobi" on the class that has started.
    const dialog = await openClassBookings(page, started);
    const makeup = dialog
      .getByRole('group', { name: `Obecność: ${name}` })
      .getByRole('button', { name: 'Odrobi' });
    await makeup.click();
    await expect(makeup).toHaveAttribute('aria-pressed', 'true');
    await expect(dialog.getByText(/Odrobią: 1 ·/)).toBeVisible();
    await dialog.getByRole('button', { name: 'Zamknij' }).click();

    // Member: the missed class keeps its entry - and Moje zajęcia says a class is owed.
    await memberPage.goto('/');
    const cards = memberPage.getByRole('region', { name: 'Twoje zajęcia i karnet' });
    await expect(cards.getByText(/^4\s*z 5$/)).toBeVisible();
    await memberPage.goto('/my-classes');
    await expect(memberPage.getByText('Do odrobienia: 1')).toBeVisible();

    // Trainer: book the makeup from Odrabianie into the class ahead.
    await page.goto('/makeups');
    const row = page.getByRole('listitem').filter({ hasText: name });
    await row.getByRole('button', { name: 'Zapisz', exact: true }).click();
    const picker = page.getByRole('dialog', { name: `Odrabianie: ${name}` });
    await picker
      .getByRole('listitem')
      .filter({ hasText: makeupClass.name })
      .getByRole('button', { name: 'Zapisz' })
      .click();
    await expect(picker).toBeHidden();
    await expect(row.getByText('Zaplanowane')).toBeVisible();

    // Member: the makeup is on the list, labelled, and the karnet still reads 4 of 5.
    await memberPage.goto('/');
    await expect(cards.getByText(/^4\s*z 5$/)).toBeVisible();
    await memberPage.goto('/my-classes');
    const upcoming = memberPage
      .getByRole('tabpanel', { name: 'Nadchodzące' })
      .getByRole('listitem')
      .filter({ hasText: makeupClass.name });
    await expect(upcoming.getByText('Odrabianie')).toBeVisible();
    await expect(memberPage.getByText('Do odrobienia: 1')).toBeHidden();
  } finally {
    await trainer.close();
    await member.close();
  }
});
