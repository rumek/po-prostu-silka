/**
 * Class start times no parallel spec or re-run will share.
 *
 * The overlap rule is club-wide (ClassStore.HasTimeConflictAsync): any two scheduled classes whose
 * times intersect collide, whoever created them. A fixed slot ("an hour from now") therefore passes
 * once and collides on the second parallel spec. A random slot collides rarely, and createClass draws
 * again on `time_conflict` rather than failing.
 *
 * Tomorrow or later, never today: a class that starts mid-test closes its bookings (`class_started`).
 * The one exception is a class that is MEANT to have started (attendance): it is booked in the future
 * and then moved to randomPastClassStart by Club.startClass - never waited for.
 */
import { Page } from '@playwright/test';

/** 07:00 to 20:45 on a 15-minute grid, inside the calendar's visible hours. */
const FIRST_SLOT_MINUTES = 7 * 60;
const SLOT_COUNT = (21 * 60 - FIRST_SLOT_MINUTES) / 15;

/** A random slot on the day `dayOffset` days from today, in this process's local time. */
function randomSlotOnDay(dayOffset: number): Date {
  const start = new Date();
  start.setDate(start.getDate() + dayOffset);
  const minutes = FIRST_SLOT_MINUTES + Math.floor(Math.random() * SLOT_COUNT) * 15;
  start.setHours(Math.floor(minutes / 60), minutes % 60, 0, 0);
  return start;
}

/** A random start from tomorrow to six days ahead, in this process's local time. */
export function randomClassStart(): Date {
  return randomSlotOnDay(1 + Math.floor(Math.random() * 6));
}

/**
 * A random start from yesterday back to six days ago - a class that has already started. Never
 * today: a slot later today would not have started yet.
 */
export function randomPastClassStart(): Date {
  return randomSlotOnDay(-1 - Math.floor(Math.random() * 6));
}

/** Midnight of the Monday that starts `date`'s week - the calendar's weeks start on Monday. */
function mondayOf(date: Date): Date {
  const monday = new Date(date);
  monday.setHours(0, 0, 0, 0);
  monday.setDate(monday.getDate() - ((monday.getDay() + 6) % 7));
  return monday;
}

/**
 * Brings `startsAt` into view in the week calendar or the phone's week strip, both of which open on
 * the current week. A start from randomClassStart is at most one week ahead and one from
 * randomPastClassStart at most one week behind, so at most one step either way is needed.
 */
export async function showWeekOf(page: Page, startsAt: Date): Promise<void> {
  const target = mondayOf(startsAt).getTime();
  const current = mondayOf(new Date()).getTime();
  if (target > current) {
    await page.getByRole('button', { name: 'Następny tydzień' }).click();
  } else if (target < current) {
    await page.getByRole('button', { name: 'Poprzedni tydzień' }).click();
  }
}
