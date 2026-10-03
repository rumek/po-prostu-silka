import { DatePipe } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { bookingFailureMessage } from '../../core/scheduling/booking-failure';
import { RosterReport as Report } from '../../core/scheduling/roster.models';
import { Icon } from '../icons/icon';
import { groupRosterLines } from './roster-lines';

/**
 * Who an automatic roster booking could NOT book, and why (S-37).
 *
 * <h2>A panel, not a toast</h2>
 *
 * The success count goes in the screen's own toast; the skips are a list the admin acts on — call the
 * member about a karnet, free a spot — so they stay on screen until dismissed. Informational screen
 * content is `.notice` (AGENTS.md, "The presentational kit"), and `role="status"` announces it without
 * interrupting.
 *
 * <h2>No words of its own</h2>
 *
 * Beyond its heading, every sentence is the booking failure table's (S-19): a skip carries a booking
 * refusal reason, and the panel only groups them — one line per member and reason, with the dates.
 *
 * Renders nothing when nothing was skipped, so a caller can hand it every report it receives.
 */
@Component({
  imports: [DatePipe, Icon],
  selector: 'app-roster-report',
  styleUrl: './roster-report.scss',
  templateUrl: './roster-report.html',
})
export class RosterReport {
  readonly report = input.required<Report>();

  readonly dismissed = output<void>();

  protected readonly lines = computed(() => groupRosterLines(this.report().skipped));

  protected readonly message = bookingFailureMessage;
}
