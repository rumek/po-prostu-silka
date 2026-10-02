import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MembershipPassView } from '../admin/member-admin.models';

/**
 * Marks a karnet paid or unpaid (pass-paid-flag) — `PUT /api/passes/{id}/paid`, the one write the
 * admin's karnet screen and the trainer's share.
 *
 * Its own service rather than a method on `MemberAdminService`, because the route is TrainerOrAdmin:
 * the trainer's screen must not have to inject the admin's service to reach it.
 *
 * An EXPLICIT value, never a toggle — a date marks it paid on that club-local day, `null` marks it
 * unpaid — so two staff acting at once converge instead of flipping each other back.
 */
@Injectable({ providedIn: 'root' })
export class PassPaymentService {
  private readonly http = inject(HttpClient);

  setPaid(passId: string, paidAt: string | null): Promise<MembershipPassView> {
    return firstValueFrom(
      this.http.put<MembershipPassView>(`/api/passes/${encodeURIComponent(passId)}/paid`, {
        paidAt,
      }),
    );
  }
}
