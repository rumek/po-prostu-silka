/**
 * Risk: a trainer's booking made in the bookings overlay is not what the member then sees - the class
 * missing from their Start, or the karnet not one entry down (BOOK-01; test-plan risks #2 and #6: the
 * staff-only booking door, and entries derived from active bookings).
 * Seed: seed.spec.ts. Acts as the E2E trainer on a class they instruct, at a phone width
 * (support/schedule.ts), then reads the member's Start in the member's own session.
 *
 * Creates a member `E2E zapis <ts>` with an `@example.test` account and a 5-entry karnet, and a class
 * instructed by the E2E trainer. The `club` fixture cancels the (now booked) class and deactivates the
 * type; the cancelled class, the member with its karnet and account stay behind.
 */
import { memberPassword, trainerCredentials } from './credentials';
import { uniqueSuffix } from './support/club';
import { expect, test } from './support/fixtures';
import { addToClass, openClassBookings, PHONE } from './support/schedule';
import { signedInContext } from './support/sessions';

test.use(PHONE);

test('a trainer booking reaches the member start with one entry fewer', async ({
  browser,
  club,
}) => {
  // Arrange: a member with an account and a karnet, then an empty class (karnet first - see CLAUDE.md).
  const suffix = uniqueSuffix();
  const name = `E2E zapis ${suffix}`;
  const email = `e2e-${suffix}@example.test`;
  const memberId = await club.createMember(name);
  await club.registerMember(memberId, email, memberPassword);
  await club.issuePass(memberId, 5);
  const created = await club.createClass(`E2E klasa zapisu ${suffix}`);

  // Trainer: open the class and book the member through the overlay.
  const trainer = await signedInContext(browser, trainerCredentials);
  try {
    const page = await trainer.newPage();
    const dialog = await openClassBookings(page, created);
    await addToClass(dialog, name);
    // On the roster (a list row, not the select's option), and a seat taken.
    await expect(dialog.getByRole('listitem').filter({ hasText: name })).toBeVisible();
    await expect(dialog.getByText(`1 / ${created.capacity} miejsc zajętych`)).toBeVisible();
  } finally {
    await trainer.close();
  }

  // Member: Start shows the class, and the karnet one entry down.
  const member = await signedInContext(browser, { email, password: memberPassword });
  try {
    const page = await member.newPage();
    await page.goto('/');
    const cards = page.getByRole('region', { name: 'Twoje zajęcia i karnet' });
    await expect(cards.getByText(created.name)).toBeVisible();
    await expect(cards.getByText(/^4\s*z 5$/)).toBeVisible();
  } finally {
    await member.close();
  }
});
