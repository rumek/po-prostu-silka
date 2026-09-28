/**
 * Risk: the invitation link an admin copies does not claim THAT record - registration produces a
 * fresh, empty member, or the new session lands somewhere other than a Start showing what the club
 * recorded (REG-01; test-plan risk #3, the claim-only registration door).
 * Seed: seed.spec.ts. The admin half runs as the seeded admin (`page`); the visitor half runs in an
 * anonymous context whose register call carries its own client address (support/sessions.ts).
 *
 * Creates a member `E2E zaproszenie <ts>` with a 5-entry karnet, and a class (instructed by the E2E
 * trainer) with that member booked. The `club` fixture cancels the class (a booked class is history
 * and cannot be deleted) and deactivates the type; the cancelled class, the member with its karnet
 * and its `@example.test` account stay behind.
 */
import { memberPassword } from './credentials';
import { expect, test } from './support/fixtures';
import { anonymousContext } from './support/sessions';

test.use({ permissions: ['clipboard-read', 'clipboard-write'] });

test('an invitation link claims the recorded member with their karnet and booking', async ({
  page,
  browser,
  club,
}) => {
  // Arrange: a member the club recorded at the desk, with a karnet and a booking already on it.
  // The karnet goes first - the class's removal must release the booking before it is revoked.
  const suffix = Date.now();
  const name = `E2E zaproszenie ${suffix}`;
  const memberId = await club.createMember(name);
  await club.issuePass(memberId, 5);
  const created = await club.createClass(`E2E klasa zaproszenia ${suffix}`);
  await club.book(created.id, memberId);

  // Admin: find the member, generate the code and copy the invitation link.
  await page.goto('/admin/members');
  await page.getByRole('searchbox', { name: 'Szukaj' }).fill(name);
  await page.getByRole('button', { name: `Akcje — ${name}` }).click();
  await page.getByRole('menuitem', { name: 'Wygeneruj kod klubowicza' }).click();
  await page.getByRole('button', { name: 'Kopiuj link' }).click();
  await expect(page.getByText('Skopiowano.')).toBeVisible();
  const link = await page.evaluate(() => navigator.clipboard.readText());

  // Visitor: open the link signed out and register with a new address.
  const visitor = await anonymousContext(browser);
  try {
    const visitorPage = await visitor.newPage();
    await visitorPage.goto(link);
    // The invitation guard lets the visitor in only with a code the link actually carries.
    await expect(visitorPage.getByRole('heading', { level: 1, name: 'Załóż konto' })).toBeVisible();
    await visitorPage.getByLabel('Adres e-mail').fill(`e2e-${suffix}@example.test`);
    await visitorPage.getByLabel('Hasło', { exact: true }).fill(memberPassword);
    await visitorPage.getByRole('button', { name: 'Załóż konto' }).click();

    // The new session lands on Start holding what the club recorded on THAT member: the booked
    // class, and the karnet with one entry already held by that booking. A fresh, empty member
    // would show both cards' empty states. (The greeting names only the first word, "E2E", so it
    // identifies nobody.)
    await visitorPage.waitForURL('/');
    const cards = visitorPage.getByRole('region', { name: 'Twoje zajęcia i karnet' });
    await expect(cards.getByText(created.name)).toBeVisible();
    await expect(cards.getByText(/^4\s*z 5$/)).toBeVisible();
  } finally {
    await visitor.close();
  }
});
