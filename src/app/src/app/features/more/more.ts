import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

/**
 * Everything that does not earn a tab in the bottom bar (S-12).
 *
 * WHY THIS SCREEN EXISTS. The bar holds five fixed tabs and the same five for every role — that is
 * what keeps a role-visibility matrix out of it, which is the bug class the S-01 review caught (F5:
 * a nav condition that disagreed with its guard showed an admin a link that bounced them). Every
 * role-conditional destination lands here instead, in ONE place that can be tested as a unit.
 *
 * It also gives logout somewhere to live. Logout mutates server state, so it stays a <button> and
 * can never be a tab or a link — nothing that a browser may prefetch or a member may middle-click
 * into a new tab.
 *
 * THE CONDITIONS BELOW MUST MATCH THEIR GUARDS EXACTLY. adminGuard is `isAdmin() && isActive()`;
 * trainerGuard is `(isTrainer() || isAdmin()) && isActive()`. A link shown on a weaker condition than
 * its guard is a link that bounces the member back to `/`, which is precisely the confusion this
 * comment exists to prevent. The API applies the same policies at the endpoint groups regardless.
 *
 * The profile entry is the deliberate exception: it is gated on isAuthenticated() alone, NOT
 * isActive(). A Pending member needs `/profile` to supply the contact details S-13 made mandatory,
 * and its route makes the same choice for the same reason.
 */
@Component({
  imports: [RouterLink, RouterLinkActive],
  selector: 'app-more',
  styleUrl: './more.scss',
  templateUrl: './more.html',
})
export class More {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Shows the panel section at all — cheaper to read than repeating the pair on every entry. */
  protected canSeePanel(): boolean {
    return (this.auth.isTrainer() || this.auth.isAdmin()) && this.auth.isActive();
  }

  protected isAdmin(): boolean {
    return this.auth.isAdmin() && this.auth.isActive();
  }

  protected isTrainerOrAdmin(): boolean {
    return (this.auth.isTrainer() || this.auth.isAdmin()) && this.auth.isActive();
  }

  protected async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }
}
