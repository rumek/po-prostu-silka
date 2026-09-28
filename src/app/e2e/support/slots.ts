/**
 * Class start times no parallel spec or re-run will share.
 *
 * The overlap rule is club-wide (ClassStore.HasTimeConflictAsync): any two scheduled classes whose
 * times intersect collide, whoever created them. A fixed slot ("an hour from now") therefore passes
 * once and collides on the second parallel spec. A random slot collides rarely, and createClass draws
 * again on `time_conflict` rather than failing.
 *
 * Tomorrow or later, never today: a class that starts mid-test closes its bookings (`class_started`).
 */
import { Page } from '@playwright/test';

/** 07:00 to 20:45 on a 15-minute grid, inside the calendar's visible hours. */
const FIRST_SLOT_MINUTES = 7 * 60;
const SLOT_COUNT = (21 * 60 - FIRST_SLOT_MINUTES) / 15;

/** A random start from tomorrow to six days ahead, in this process's local time. */
export function randomClassStart(): Date {
  const start = new Date();
  start.setDate(start.getDate() + 1 + Math.floor(Math.random() * 6));
  const minutes = FIRST_SLOT_MINUTES + Math.floor(Math.random() * SLOT_COUNT) * 15;
  start.setHours(Math.floor(minutes / 60), minutes % 60, 0, 0);
  return start;
}

/** Midnight of the Monday that starts `date`'s week - the calendar's weeks start on Monday. */
function mondayOf(date: Date): Date {
  const monday = new Date(date);
  monday.setHours(0, 0, 0, 0);
  monday.setDate(monday.getDate() - ((monday.getDay() + 6) % 7));
  return monday;
}

/**
 * Brings `startsAt` into view in the desktop week calendar, which opens on the current week. A start
 * from randomClassStart is at most one week ahead, so at most one step is needed.
 */
export async function showWeekOf(page: Page, startsAt: Date): Promise<void> {
  if (mondayOf(startsAt).getTime() !== mondayOf(new Date()).getTime()) {
    await page.getByRole('button', { name: 'Następny tydzień' }).click();
  }
}
