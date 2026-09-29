/**
 * Risk: a booking for someone without a valid karnet is not refused where staff make it - the real
 * API's `no_valid_pass` does not reach the overlay as its sentence, or a spot is taken anyway
 * (BOOK-03; test-plan risk #2, the karnet gate on booking).
 * Seed: seed.spec.ts. Acts as the E2E trainer on a class they instruct, at a phone width
 * (support/schedule.ts).
 *
 * Creates a member `E2E bez karnetu <ts>` (no account, no karnet) and a class instructed by the E2E
 * trainer. Nothing gets booked, so the `club` fixture deletes the class and deactivates the type; the
 * member stays behind.
 */
import { trainerCredentials } from './credentials';
import { uniqueSuffix } from './support/club';
import { expect, test } from './support/fixtures';
import { addToClass, openClassBookings, PHONE } from './support/schedule';
import { signedInContext } from './support/sessions';

test.use(PHONE);

test('booking a member without a valid karnet is refused in the overlay', async ({
  browser,
  club,
}) => {
  // Arrange: a member the club recorded, with no karnet, and an empty class.
  const suffix = uniqueSuffix();
  const name = `E2E bez karnetu ${suffix}`;
  await club.createMember(name);
  const created = await club.createClass(`E2E klasa odmowy ${suffix}`);

  const trainer = await signedInContext(browser, trainerCredentials);
  try {
    const page = await trainer.newPage();
    const dialog = await openClassBookings(page, created);

    // Try to book them - an accountless member is listed "— bez konta".
    await addToClass(dialog, name, `${name} — bez konta`);

    // The API's refusal reaches the overlay as its sentence, and nobody was booked.
    await expect(dialog.getByRole('alert')).toHaveText(
      'Ta osoba nie ma karnetu ważnego w dniu tych zajęć.',
    );
    await expect(dialog.getByText('Nikt nie jest jeszcze zapisany na te zajęcia.')).toBeVisible();
    await expect(dialog.getByText(`0 / ${created.capacity} miejsc zajętych`)).toBeVisible();
  } finally {
    await trainer.close();
  }
});
