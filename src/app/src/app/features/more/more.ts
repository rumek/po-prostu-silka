import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { personaOf } from '../../core/auth/persona';
import { DESK_MEDIA_QUERY } from '../../core/layout/breakpoints';
import { mediaQuerySignal } from '../../core/layout/media-query';
import { navigationFor } from '../../core/layout/navigation';

/**
 * Everything that does not earn a tab in the bottom bar (S-12).
 *
 * WHY THIS SCREEN EXISTS. The bar holds at most five tabs, and a persona can have more
 * destinations than that — the admin has six. The overflow lands here: the persona's `more` list
 * from `core/layout/navigation.ts`, the same table the header and the bar read (S-25). Before S-25
 * this file carried its own role conditions for the panel; the table replaced them, so a link here
 * can no longer disagree with its guard (the S-01 F5 bug class) or with the header.
 *
 * It also gives logout somewhere to live. Logout mutates server state, so it stays a <button> and
 * can never be a tab or a link — nothing that a browser may prefetch or a member may middle-click
 * into a new tab.
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

  private readonly desk = mediaQuerySignal(DESK_MEDIA_QUERY, true);

  /** The persona's overflow — empty for a member, a trainer, and an account with no persona. */
  protected readonly panel = computed(
    () => navigationFor(personaOf(this.auth.user()), this.desk()).more,
  );

  protected async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }
}
