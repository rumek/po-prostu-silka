import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth/auth.service';
import { personaOf } from './core/auth/persona';
import { DESK_MEDIA_QUERY } from './core/layout/breakpoints';
import { mediaQuerySignal } from './core/layout/media-query';
import { navigationFor } from './core/layout/navigation';
import { ScreenTitle } from './core/layout/screen-title';
import { Up } from './core/layout/up';
import { PushPrompt } from './features/notifications/push-prompt';
import { BottomNav } from './shared/bottom-nav/bottom-nav';
import { Icon } from './shared/icons/icon';
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
 *
 * THE PHONE'S APP BAR READS `ScreenTitle` (mobile-native-feel): the route table names every screen
 * and says how deep it sits, so the bar shows the logo on a brand screen, the title on a tab, and a
 * back arrow plus the title on a child.
 */
@Component({
  imports: [BottomNav, Icon, PushPrompt, RouterOutlet, RouterLink, RouterLinkActive, ToastHost],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  private readonly desk = mediaQuerySignal(DESK_MEDIA_QUERY, true);

  protected readonly nav = computed(() => navigationFor(personaOf(this.auth.user()), this.desk()));

  protected readonly screen = inject(ScreenTitle);

  /** Signed out, the header is the logo whatever the route says — there is no app to be inside yet. */
  protected readonly titled = computed(
    () => this.auth.isAuthenticated() && this.screen.level() !== 'brand' && !!this.screen.title(),
  );

  // Injected here, in the root component, so its history model starts with the first navigation.
  private readonly upward = inject(Up);

  protected up(): void {
    this.upward.to(this.screen.parent() ?? '/');
  }

  protected async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }
}
