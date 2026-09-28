/**
 * Risk: an absence a trainer records in the bookings overlay does not reach the member - the entry
 * not back on their karnet, or the class not read as absent in Historia (ATT-01, ATT-02, MBR-02;
 * test-plan risk #6: entries derived from active bookings, which an absence must stop counting).
 * Seed: seed.spec.ts. The class is booked in the future and then moved into the past week
 * (club.startClass), so it has started without the spec waiting for a clock. The trainer marks at a
 * phone width (support/schedule.ts); the member reads Start and Historia in their own session.
 *
 * Creates a member `E2E nieobecność <ts>` with an `@example.test` account and a 5-entry karnet valid
 * from a week ago, and a class instructed by the E2E trainer. The class has started, so the API keeps
 * it as attendance history: it stays behind, as do the member, the account and the karnet. The class
 * type is deactivated.
 */
import { memberPassword, trainerCredentials } from './credentials';
import { uniqueSuffix } from './support/club';
import { expect, test } from './support/fixtures';
import { openClassBookings, PHONE } from './support/schedule';
import { signedInContext } from './support/sessions';

test.use(PHONE);

test('an absence recorded by the trainer returns the entry and reads absent in history', async ({
  browser,
  club,
}) => {
  // Arrange: karnet first (see CLAUDE.md), covering the past week; book while the class is ahead,
  // then move it into the past - a started class takes no booking.
  const suffix = uniqueSuffix();
  const name = `E2E nieobecność ${suffix}`;
  const email = `e2e-nieobecnosc-${suffix}@example.test`;
  const memberId = await club.createMember(name);
  await club.registerMember(memberId, email, memberPassword);
  await club.issuePass(memberId, 5, 30, { validFromDaysAgo: 7 });
  const created = await club.createClass(`E2E klasa obecności ${suffix}`);
  await club.book(created.id, memberId);
  const started = await club.startClass(created);

  const member = await signedInContext(browser, { email, password: memberPassword });
  try {
    const memberPage = await member.newPage();

    // Member, before: the started, unmarked class still holds its entry.
    await memberPage.goto('/');
    const cards = memberPage.getByRole('region', { name: 'Twoje zajęcia i karnet' });
    await expect(cards.getByText(/^4\s*z 5$/)).toBeVisible();

    // Trainer: mark the member absent on the class that has started.
    const trainer = await signedInContext(browser, trainerCredentials);
    try {
      const page = await trainer.newPage();
      const dialog = await openClassBookings(page, started);
      const absent = dialog
        .getByRole('group', { name: `Obecność: ${name}` })
        .getByRole('button', { name: 'Nieobecny' });
      await absent.click();
      await expect(absent).toHaveAttribute('aria-pressed', 'true');
      await expect(dialog.getByText(/Nieobecni: 1/)).toBeVisible();
    } finally {
      await trainer.close();
    }

    // Member, after: the entry is back, and Historia reads the class as absent.
    await memberPage.reload();
    await expect(cards.getByText(/^5\s*z 5$/)).toBeVisible();

    await memberPage.goto('/my-classes');
    await memberPage.getByRole('tab', { name: 'Historia' }).click();
    const history = memberPage.getByRole('tabpanel', { name: 'Historia' });
    const row = history.getByRole('listitem').filter({ hasText: started.name });
    await expect(row.getByText('Nieobecny')).toBeVisible();
  } finally {
    await member.close();
  }
});
