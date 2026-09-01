import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';

type CheckOutcome = 'idle' | 'still-pending' | 'failed';

/**
 * The awaiting-approval screen — where a Pending member lives until an admin lets them in.
 *
 * The route carries authGuard ONLY, deliberately: activeMemberGuard redirects Pending members here,
 * so listing it would be a loop. That is also why the already-approved redirect below lives in the
 * component rather than in a guard.
 */
@Component({
  selector: 'app-pending',
  styleUrl: './pending.scss',
  templateUrl: './pending.html',
})
export class Pending implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly checking = signal(false);
  protected readonly outcome = signal<CheckOutcome>('idle');

  /**
   * An already-approved member who types /pending would otherwise sit on a screen telling them to
   * await approval they already have.
   */
  async ngOnInit(): Promise<void> {
    if (this.auth.isActive()) {
      await this.router.navigate(['/']);
    }
  }

  /**
   * MUST call refresh(), NOT loadCurrentUser().
   *
   * /me reads the database and would report Active while the auth cookie's account_status claim
   * still said Pending — the claim is re-minted only every 30 minutes. Navigating on the strength of
   * /me would drop the member into an app where every ActiveMember request answers 403 for up to
   * half an hour. /api/auth/refresh re-mints the claim, which is what actually makes them Active as
   * far as the API is concerned.
   */
  protected async check(): Promise<void> {
    this.checking.set(true);
    this.outcome.set('idle');

    try {
      const user = await this.auth.refresh();

      if (user.status === 'Active') {
        await this.router.navigate(['/']);
        return;
      }

      // Say so. A button that appears to do nothing is worse than one reporting no change.
      this.outcome.set('still-pending');
    } catch {
      this.outcome.set('failed');
    } finally {
      this.checking.set(false);
    }
  }
}
