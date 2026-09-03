import { formatDate } from '@angular/common';
import { Injectable } from '@angular/core';
import { CalendarAngularDateFormatter, DateFormatterParams } from 'angular-calendar';

/**
 * The library's default formatter, with the two formats that are wrong for Polish replaced.
 *
 * `CalendarAngularDateFormatter` hardcodes AMERICAN patterns and then feeds them to Angular's
 * `formatDate` with our locale — `'h a'` for the hour column and `'MMM d'` for the day headers. The
 * locale translates the WORDS but not the pattern, so a Polish calendar came out reading "6 AM" and
 * "mar 4". Both are simply not how Poland writes a time or a date.
 *
 * Subclassing rather than reimplementing `CalendarDateFormatter`: everything else the library
 * formats — weekday names, the month view labels — is already right once the locale is applied, and
 * an interface implemented from scratch would silently lose those on a library upgrade.
 *
 * Registered on {@link ScheduleCalendar} itself, not application-wide: the library must not enter the
 * initial bundle (see the note on that component's providers).
 */
@Injectable()
export class PolishCalendarDateFormatter extends CalendarAngularDateFormatter {
  /** 24-hour, zero-padded, with the minutes — `06:00`, not `6 AM`. */
  override weekViewHour({ date, locale }: DateFormatterParams): string {
    return formatDate(date, 'HH:mm', locale!);
  }

  override dayViewHour({ date, locale }: DateFormatterParams): string {
    return formatDate(date, 'HH:mm', locale!);
  }

  /** Day before month, as Polish writes it. */
  override weekViewColumnSubHeader({ date, locale }: DateFormatterParams): string {
    return formatDate(date, 'd MMM', locale!);
  }
}
