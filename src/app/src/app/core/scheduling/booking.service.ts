import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ClassBooking, MyBooking } from './booking.models';
import { ScheduledClass } from './class.models';

/**
 * Booking and cancelling a spot (prd.md US-01, FR-008, FR-009, FR-010, FR-014).
 *
 * Relative /api paths, like every other service here: the SPA is served from the API's own wwwroot,
 * so these are same-origin and the auth cookie rides along.
 *
 * Nothing here catches. A refused booking has to reach the screen, which shows the reason in the
 * overlay — a member who believes they have a spot when they do not is the failure mode that matters.
 *
 * <h2>Why book and cancel answer with a class</h2>
 *
 * They return the occurrence AS IT NOW STANDS, not the booking. The client already knows it booked;
 * what it does not know is the new free-spot count, and answering with the class is what lets the
 * schedule replace the tile in place instead of refetching the week.
 */
@Injectable({ providedIn: 'root' })
export class BookingService {
  private readonly http = inject(HttpClient);

  /** Claims a spot. Resolves with the class as it now stands. */
  book(classId: string): Promise<ScheduledClass> {
    return firstValueFrom(
      this.http.post<ScheduledClass>(
        `/api/classes/${encodeURIComponent(classId)}/bookings`,
        // No body: the class is in the URL and the member is the cookie. There is nothing to send.
        null,
      ),
    );
  }

  /**
   * Releases the caller's own spot. Addressed by CLASS rather than by booking id — a member holds at
   * most one active booking per class, so the class identifies it, and the client never has to carry
   * a booking id it did not need for anything else.
   */
  cancel(classId: string): Promise<ScheduledClass> {
    return firstValueFrom(
      this.http.delete<ScheduledClass>(`/api/classes/${encodeURIComponent(classId)}/bookings/mine`),
    );
  }

  /** The caller's upcoming bookings, chronological. Upcoming only — the past is history, not a list. */
  getMine(): Promise<MyBooking[]> {
    return firstValueFrom(this.http.get<MyBooking[]>('/api/bookings/mine'));
  }

  /** Who signed up for a class (FR-014). Admin only. */
  getForClass(classId: string): Promise<ClassBooking[]> {
    return firstValueFrom(
      this.http.get<ClassBooking[]>(`/api/admin/classes/${encodeURIComponent(classId)}/bookings`),
    );
  }

  /** Releases somebody else's spot. Admin only. */
  async cancelAsAdmin(classId: string, bookingId: string): Promise<void> {
    await firstValueFrom(
      this.http.delete<void>(
        `/api/admin/classes/${encodeURIComponent(classId)}/bookings/${encodeURIComponent(bookingId)}`,
      ),
    );
  }
}
