/**
 * Risk: on a phone, every bottom-bar tap pushed a history entry, so Android's back gesture walked
 * back through every tab visited instead of returning to Start (mobile-native-feel, Phase 2).
 * Seed: seed.spec.ts. Runs as the seeded admin — the behaviour is persona-agnostic, and the admin's
 * phone bar is Start | Grafik | Członkowie | Ćwiczenia | Więcej.
 *
 * Only reads, so there is nothing to clean up.
 */
import { expect, test } from '@playwright/test';

test.use({ viewport: { width: 390, height: 844 } });

test('back from a tab reached through other tabs lands on Start', async ({ page }) => {
  await page.goto('/');
  const bar = page.getByRole('navigation', { name: 'Nawigacja główna' });
  await expect(bar).toBeVisible();

  await bar.getByRole('link', { name: 'Członkowie' }).click();
  await page.waitForURL('/admin/members');

  await bar.getByRole('link', { name: 'Ćwiczenia' }).click();
  await page.waitForURL('/admin/exercises');

  await page.goBack();

  await expect(page).toHaveURL('/');
  await expect(bar.getByRole('link', { name: 'Start' })).toHaveAttribute('aria-current', 'page');
});
