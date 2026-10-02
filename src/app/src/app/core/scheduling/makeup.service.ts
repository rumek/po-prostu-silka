import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ScheduledClass } from './class.models';
import { MakeupItem, MakeupPage, MyMakeups } from './makeup.models';

/**
 * Makeups (S-36). The staff routes are TrainerOrAdmin with no per-class narrowing — any trainer
 * arranges any member's makeup in any class — and `mine` is the member's own.
 */
@Injectable({ providedIn: 'root' })
export class MakeupService {
  private readonly http = inject(HttpClient);

  /** The staff list: open and planned items, or every item with `closed`. */
  list(closed: boolean, page: number): Promise<MakeupPage> {
    const params: Record<string, string> = { page: String(page) };
    if (closed) {
      params['closed'] = 'true';
    }

    return firstValueFrom(this.http.get<MakeupPage>('/api/makeups', { params }));
  }

  /** The classes an open item's makeup may go into, by start. */
  classes(absenceBookingId: string): Promise<ScheduledClass[]> {
    return firstValueFrom(
      this.http.get<ScheduledClass[]>(`${this.item(absenceBookingId)}/classes`),
    );
  }

  /** Books the free makeup into `classId`; answers with the item as it now stands. */
  book(absenceBookingId: string, classId: string): Promise<MakeupItem> {
    return firstValueFrom(
      this.http.post<MakeupItem>(`${this.item(absenceBookingId)}/booking`, { classId }),
    );
  }

  /** Releases a planned makeup before its start, which opens the item again. */
  release(absenceBookingId: string): Promise<MakeupItem> {
    return firstValueFrom(this.http.delete<MakeupItem>(`${this.item(absenceBookingId)}/booking`));
  }

  /** Closes an open item as "nie odrobił". */
  close(absenceBookingId: string): Promise<MakeupItem> {
    return firstValueFrom(this.http.put<MakeupItem>(`${this.item(absenceBookingId)}/closed`, null));
  }

  /** Reopens an item closed by hand, while its deadline allows. */
  reopen(absenceBookingId: string): Promise<MakeupItem> {
    return firstValueFrom(this.http.delete<MakeupItem>(`${this.item(absenceBookingId)}/closed`));
  }

  /** The signed-in member's open items. Members only — staff never request it. */
  mine(): Promise<MyMakeups> {
    return firstValueFrom(this.http.get<MyMakeups>('/api/makeups/mine'));
  }

  private item(absenceBookingId: string): string {
    return `/api/makeups/${encodeURIComponent(absenceBookingId)}`;
  }
}
