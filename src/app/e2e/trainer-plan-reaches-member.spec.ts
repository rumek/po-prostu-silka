/**
 * Risk: the plan a trainer builds is not the plan the member reads - the order set by dragging a row
 * lost on save, a row's parameters left behind on another exercise after the drag, or the member's
 * card opening the wrong exercise (PLAN-01, MBR-05, MBR-06). The drag is cdk drag-drop, which exists
 * only in the rendered UI and has no unit test of its own.
 * Seed: seed.spec.ts. Builds the plan as the E2E trainer at a desktop width, tall enough that every
 * row is in view for the drag; reads Mój plan and the exercise detail in the member's own session.
 *
 * Creates a member `E2E plan <ts>` with an `@example.test` account, three exercises
 * `E2E ćw A|B|C <ts>` and a plan. The `club` fixture deactivates the exercises; the member, the
 * account and the plan (which has no delete) stay behind.
 */
import { Locator, Page } from '@playwright/test';
import { memberPassword, trainerCredentials } from './credentials';
import { uniqueSuffix } from './support/club';
import { expect, test } from './support/fixtures';
import { signedInContext } from './support/sessions';

test.use({ viewport: { width: 1280, height: 1600 } });

/** The builder's rows, in plan order: each is the list item that carries a "Serie" field. */
function builderRows(page: Page): Locator {
  return page.getByRole('listitem').filter({ has: page.getByLabel('Serie') });
}

test('a plan a trainer builds and reorders reaches the member in that order', async ({
  browser,
  club,
}) => {
  // Arrange: a member with an account and no plan, and three exercises only this run can match.
  const suffix = uniqueSuffix();
  const name = `E2E plan ${suffix}`;
  const email = `e2e-plan-${suffix}@example.test`;
  const planName = `E2E plan treningowy ${suffix}`;
  const [a, b, c] = ['A', 'B', 'C'].map((letter) => `E2E ćw ${letter} ${suffix}`);
  const description = `Opis ćwiczenia A ${suffix}`;
  const execution = `Wykonanie ćwiczenia A ${suffix}`;

  const memberId = await club.createMember(name);
  await club.registerMember(memberId, email, memberPassword);
  await club.createExercise(a, { description, execution });
  await club.createExercise(b);
  await club.createExercise(c);

  const trainer = await signedInContext(browser, trainerCredentials);
  try {
    const page = await trainer.newPage();

    // Trainer: open the member from Członkowie.
    await page.goto('/trainer/members');
    await page.getByRole('searchbox', { name: 'Szukaj członka' }).fill(name);
    await page.getByRole('link', { name, exact: true }).click();
    await expect(page.getByRole('heading', { level: 1, name: `Plan — ${name}` })).toBeVisible();

    // Name the plan, then add A, B, C from the library in that order.
    await page.getByLabel('Nazwa planu').fill(planName);
    await page.getByRole('searchbox', { name: 'Szukaj ćwiczenia' }).fill(String(suffix));
    for (const exercise of [a, b, c]) {
      await page.getByRole('button', { name: exercise, exact: true }).click();
    }
    const rows = builderRows(page);
    await expect(rows).toHaveCount(3);

    // One meaningful parameter per row, and a note on C.
    const rowOf = (exercise: string) => rows.filter({ hasText: exercise });
    await rowOf(a).getByLabel('Serie').fill('3');
    await rowOf(a).getByLabel('Powtórzenia').fill('8-12');
    await rowOf(b).getByLabel('Ciężar (kg)').fill('20');
    await rowOf(c).getByLabel('Czas (s)').fill('45');
    await rowOf(c).getByLabel('Uwagi').fill(`Notatka ${suffix}`);

    // Drag C by its handle above A. cdk only starts a drag past its threshold and sorts on the
    // moves it sees, so the pointer travels in steps rather than jumping.
    await rowOf(c).getByTitle('Przeciągnij').hover();
    await page.mouse.down();
    const top = (await rows.first().boundingBox())!;
    await page.mouse.move(top.x + top.width / 2, top.y + 10, { steps: 20 });
    await page.mouse.up();

    await expect(rows.nth(0)).toContainText(c);
    await expect(rows.nth(1)).toContainText(a);
    await expect(rows.nth(2)).toContainText(b);
    // The parameters moved with their exercise, not with the position.
    await expect(rowOf(c).getByLabel('Czas (s)')).toHaveValue('45');
    await expect(rowOf(a).getByLabel('Powtórzenia')).toHaveValue('8-12');

    // Waits on the save itself rather than its toast: the toast is aria-hidden and its words reach
    // assistive tech through a second, visually hidden live region, so the text is on the page twice.
    const saved = page.waitForResponse(
      (response) =>
        response.url().endsWith('/api/trainer/plans') && response.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'Przypisz plan' }).click();
    expect((await saved).ok(), 'the plan was saved').toBeTruthy();

    // Członkowie now names the member's plan.
    await page.goto('/trainer/members');
    await page.getByRole('searchbox', { name: 'Szukaj członka' }).fill(name);
    await expect(
      page.getByRole('listitem').filter({ hasText: name }).getByText(`Plan: ${planName}`),
    ).toBeVisible();
  } finally {
    await trainer.close();
  }

  const member = await signedInContext(browser, { email, password: memberPassword });
  try {
    const page = await member.newPage();

    // Member: Mój plan, in the dragged order, each exercise with its own parameters.
    await page.goto('/my-plan');
    await expect(page.getByRole('heading', { name: planName })).toBeVisible();

    const details = page.getByRole('link', { name: /^Opis ćwiczenia: / });
    await expect(details).toHaveCount(3);
    await expect(details.nth(0)).toHaveAccessibleName(`Opis ćwiczenia: ${c}`);
    await expect(details.nth(1)).toHaveAccessibleName(`Opis ćwiczenia: ${a}`);
    await expect(details.nth(2)).toHaveAccessibleName(`Opis ćwiczenia: ${b}`);

    const card = (exercise: string) =>
      page
        .getByRole('listitem')
        .filter({ has: page.getByRole('link', { name: `Opis ćwiczenia: ${exercise}` }) });
    const parameter = (exercise: string, label: string) =>
      card(exercise).getByRole('listitem').filter({ hasText: label });
    await expect(parameter(c, 'Czas')).toContainText('45 s');
    await expect(card(c).getByText(`Notatka ${suffix}`)).toBeVisible();
    await expect(parameter(a, 'Serie')).toContainText('3');
    await expect(parameter(a, 'Powtórzenia')).toContainText('8-12');
    await expect(parameter(b, 'Ciężar')).toContainText('20 kg');

    // The card opens that exercise's detail, and up leads back to the plan.
    await details.nth(1).click();
    await expect(page.getByRole('heading', { level: 1, name: a })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Opis', exact: true })).toBeVisible();
    await expect(page.getByText(description)).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Wykonanie', exact: true })).toBeVisible();
    await expect(page.getByText(execution)).toBeVisible();

    await page.getByRole('link', { name: 'Wróć do planu' }).click();
    await page.waitForURL(/\/my-plan$/);
    await expect(page.getByRole('heading', { name: planName })).toBeVisible();
  } finally {
    await member.close();
  }
});
