/**
 * Risk: a member entered once in a group's fixed roster is not booked into the classes the group gets
 * later - the copies a weekly duplication creates - so the club is back to booking everyone by hand,
 * or the member never sees those classes (S-37 group-fixed-roster; ROSTER-01, ROSTER-05; test-plan
 * risk #6: every booking goes through the no-overbooking protocol with its karnet gate). Seed:
 * seed.spec.ts. The admin works at desk width, where the class calendar renders; the member reads Moje
 * zajęcia in their own session.
 *
 * Creates a member `E2E skład <ts>` with an `@example.test` account and a 5-entry karnet, a group
 * `E2E grupa składu <ts>` and one class of it, instructed by the E2E trainer. The two copies the UI
 * duplication makes are removed with the class (support/club.ts removeGroupClassesAfterwards): all
 * three are booked, so they are cancelled. The member, the account and the karnet stay behind; the
 * group is deactivated, and its roster stays with it.
 */
import { memberPassword } from './credentials';
import { uniqueSuffix } from './support/club';
import { expect, test } from './support/fixtures';
import { signedInContext } from './support/sessions';
import { showWeekOf } from './support/slots';

// Dates render as this process builds them (see support/schedule.ts's PHONE); the default viewport is
// a desk, which the admin's class calendar needs.
test.use({ timezoneId: Intl.DateTimeFormat().resolvedOptions().timeZone, locale: 'pl-PL' });

test('a member added to a group roster is booked into the classes later duplicated from it', async ({
  browser,
  club,
  page,
}) => {
  // Arrange: the group before the karnet, the karnet before the class (see CLAUDE.md).
  const suffix = uniqueSuffix();
  const name = `E2E skład ${suffix}`;
  const email = `e2e-sklad-${suffix}@example.test`;
  const groupName = `E2E grupa składu ${suffix}`;
  const memberId = await club.createMember(name);
  await club.registerMember(memberId, email, memberPassword);
  const groupId = await club.createGroup(groupName);
  await club.issuePass(memberId, 5, 30);
  const source = await club.createClass(groupName, { classGroupId: groupId });
  club.removeGroupClassesAfterwards(groupId, [source.id]);

  // Admin: add the member to the group's roster - they are booked into its one upcoming class.
  await page.goto(`/admin/class-groups/${groupId}/roster`);
  await expect(page.getByRole('heading', { level: 1, name: `Skład — ${groupName}` })).toBeVisible();
  await page.getByRole('searchbox', { name: 'Szukaj osoby' }).fill(name);
  const select = page.getByRole('combobox', { name: 'Osoba do dopisania' });
  await expect(select.getByRole('option', { name })).toBeAttached();
  await select.selectOption({ label: name });
  await page.getByRole('button', { name: 'Dopisz', exact: true }).click();
  const row = page.getByRole('listitem').filter({ hasText: name });
  await expect(row.getByText('zapisany na 1 z 1 zajęć')).toBeVisible();

  // Admin: duplicate the class for the next two weeks from Zajęcia.
  await page.goto('/admin/classes');
  await showWeekOf(page, source.startsAt);
  await page.getByRole('button', { name: new RegExp(groupName) }).click();
  const actions = page.getByRole('dialog', { name: groupName });
  await actions.getByRole('button', { name: 'Powiel' }).click();
  await actions.getByLabel('Na kolejne tygodnie').fill('2');
  await actions.getByRole('button', { name: 'Powiel', exact: true }).click();
  await expect(page.getByText(/Utworzono 2 kopie/)).toBeVisible();

  // Member: Moje zajęcia lists the class and both copies - nobody booked them by hand.
  const member = await signedInContext(browser, { email, password: memberPassword });
  try {
    const memberPage = await member.newPage();
    await memberPage.goto('/my-classes');
    const upcoming = memberPage
      .getByRole('tabpanel', { name: 'Nadchodzące' })
      .getByRole('listitem')
      .filter({ hasText: groupName });
    await expect(upcoming).toHaveCount(3);
  } finally {
    await member.close();
  }
});
