import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ClassBooking, MyBooking } from './booking.models';
import { ScheduledClass } from './class.models';

/**
 * Reading a member's bookings, and the staff actions that create and release them (prd.md FR-010,
 * FR-014; S-16 MP-01 and MP-02).
 *
 * <h2>A member no longer writes here</h2>
 *
 * `book()` and `cancel()` are GONE. MP-01 removed self-service booking: the karnet decides who
 * trains and the desk is what knows whether somebody holds one, so a member reads `getMine()` and
 * nothing else. The API removed the two routes outright, so re-adding methods for them would produce
 * a 405, not a 403.
 *
 * Relative /api paths, like every other service here: the SPA is served from the API's own wwwroot,
 * so these are same-origin and the auth cookie rides along.
 *
 * Nothing here catches. A refused booking has to reach the screen that asked for it — a staff member
 * who believes somebody has a spot when they do not is the failure mode that matters.
 *
 * <h2>Why bookForMember answers with a class</h2>
 *
 * It returns the occurrence AS IT NOW STANDS, not the booking. The caller already knows it booked;
 * what it does not know is the new free-spot count, and answering with the class is what lets the
 * calendar replace the tile in place instead of refetching the week. `cancelAsAdmin` deliberately
 * does not: the screen that releases a spot is a list of people and reloads that list.
 */
@Injectable({ providedIn: 'root' })
export class BookingService {
  private readonly http = inject(HttpClient);

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

  /**
   * Books somebody else in (S-14, AM-007). Admin only. Resolves with the class as it now stands,
   * exactly like `book`.
   *
   * THE ONE BOOKING CALL THAT NAMES ITS MEMBER. Every other route here takes the member from the
   * cookie; this one cannot, because the person being booked may have no login at all — which is
   * the case the route exists for. What makes it safe is the Admin policy on the server.
   */
  bookForMember(classId: string, memberId: string): Promise<ScheduledClass> {
    return firstValueFrom(
      this.http.post<ScheduledClass>(`/api/admin/classes/${encodeURIComponent(classId)}/bookings`, {
        memberId,
      }),
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
