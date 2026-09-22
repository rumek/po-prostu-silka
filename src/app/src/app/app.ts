import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth/auth.service';
import { personaOf } from './core/auth/persona';
import { DESK_MEDIA_QUERY } from './core/layout/breakpoints';
import { mediaQuerySignal } from './core/layout/media-query';
import { navigationFor } from './core/layout/navigation';
import { PushPrompt } from './features/notifications/push-prompt';
import { BottomNav } from './shared/bottom-nav/bottom-nav';
import { ToastHost } from './shared/toast/toast-host';

/**
 * The application shell: brand, the authenticated-only controls, and the routed view.
 *
 * It reads AuthService directly rather than taking inputs — the header has to react to a session
 * resolved by a guard mid-navigation, and signals already do that.
 *
 * THE MENUS COME FROM ONE TABLE (S-25). `nav` is `navigationFor(persona, desk)`; the header renders
 * its `header` list and hands `bar` to the bottom bar, and /more reads the same function. The desk
 * signal lives here, not in the table, so the table stays pure data — it decides only where the
 * admin's Grafik points. Its fallback is `true`, as for the calendar's own desk check: with no
 * viewport to measure (server, jsdom), the admin's Grafik is the management calendar.
 */
@Component({
  imports: [BottomNav, PushPrompt, RouterOutlet, RouterLink, RouterLinkActive, ToastHost],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  private readonly desk = mediaQuerySignal(DESK_MEDIA_QUERY, true);

  protected readonly nav = computed(() => navigationFor(personaOf(this.auth.user()), this.desk()));

  protected async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }
}
