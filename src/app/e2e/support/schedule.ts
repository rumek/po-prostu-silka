/**
 * Opens a class's bookings overlay from the staff schedule, the way a trainer reaches it. Specs that
 * use it run at a phone width (see PHONE), where /schedule shows one day of a week strip; the same
 * screen and path as back-closes-open-overlay.spec.ts, which drives it through the menu instead.
 */
import { expect, Locator, Page } from '@playwright/test';
import { CreatedClass } from './club';
import { showWeekOf } from './slots';

/** The viewport, timezone and locale these helpers assume: dates render as this process builds them. */
export const PHONE = {
  viewport: { width: 390, height: 844 },
  timezoneId: Intl.DateTimeFormat().resolvedOptions().timeZone,
  locale: 'pl-PL',
};

/** Goes to /schedule, brings the class's day into view, opens it, and returns the open dialog. */
export async function openClassBookings(page: Page, created: CreatedClass): Promise<Locator> {
  await page.goto('/schedule');
  await expect(page.getByRole('group', { name: 'Dni tygodnia' })).toBeVisible();
  await showWeekOf(page, created.startsAt);

  const day = created.startsAt.toLocaleDateString('pl-PL', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  });
  await page.getByRole('button', { name: day }).click();
  await page.getByRole('button', { name: new RegExp(created.name) }).click();

  const dialog = page.getByRole('dialog', { name: `Zapisani na „${created.name}”` });
  await expect(dialog).toBeVisible();
  return dialog;
}

/** Searches the overlay's "Dopisz członka" for `name`, chooses the match, and presses "Zapisz". */
export async function addToClass(dialog: Locator, name: string, optionLabel = name): Promise<void> {
  await dialog.getByRole('searchbox', { name: 'Dopisz członka' }).fill(name);
  const select = dialog.getByRole('combobox', { name: 'Osoba do dopisania' });
  await expect(select.getByRole('option', { name: optionLabel })).toBeAttached();
  await select.selectOption({ label: optionLabel });
  await dialog.getByRole('button', { name: 'Zapisz' }).click();
}
