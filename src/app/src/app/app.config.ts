import {
  ApplicationConfig,
  LOCALE_ID,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localePl from '@angular/common/locales/pl';
import { provideServiceWorker } from '@angular/service-worker';
import {
  TitleStrategy,
  provideRouter,
  withInMemoryScrolling,
  withViewTransitions,
} from '@angular/router';
import { provideClientHydration } from '@angular/platform-browser';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { InstallService } from './core/pwa/install.service';
import { ScreenTitleStrategy } from './core/layout/screen-title';
import { slideDirection } from './core/layout/view-transitions';

// Locale DATA, not i18n. D9 rules out translation machinery; this is the CLDR table DatePipe needs
// to render "1 września 2026" instead of throwing "Missing locale data for the locale pl". Without
// it the admin's "waiting since" column is a runtime error, and only on that one screen.
registerLocaleData(localePl);

export const appConfig: ApplicationConfig = {
  providers: [
    { provide: LOCALE_ID, useValue: 'pl' },
    provideBrowserGlobalErrorListeners(),
    // Screens slide in and out (S-26). The animation is the browser's View Transition, described
    // once in styles.scss; the router only starts it, and slideDirection only says which way. Not
    // the initial navigation: there is nothing on screen yet for the first screen to slide over.
    // A browser without the API navigates exactly as before — Angular checks for it.
    //
    // A new screen opens at its top and the back button returns to where the member was, as on
    // Android. Without it the scroll position carried over from the previous screen, and the
    // transition started from a page scrolled somewhere in its middle.
    provideRouter(
      routes,
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled' }),
      withViewTransitions({ skipInitialTransition: true, onViewTransitionCreated: slideDirection }),
    ),
    // Every route names itself (mobile-native-feel); this publishes the name and depth to the
    // shell's phone app bar and to document.title. See core/layout/screen-title.ts.
    { provide: TitleStrategy, useClass: ScreenTitleStrategy },
    provideClientHydration(),

    // No withCredentials and no API base URL: the SPA is served from the API's own wwwroot, so
    // relative /api/... calls are same-origin and the browser sends the auth cookie by default.
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),

    // Registered for Web Push, not for offline caching: ngsw-config.json declares empty
    // assetGroups/dataGroups because the PRD locks "no offline-first guarantee", and caching a live
    // class schedule would seed stale-data bugs into S-03/S-04.
    //
    // Disabled in dev builds - a service worker caching a dev server is a debugging trap.
    provideServiceWorker('ngsw-worker.js', { enabled: !isDevMode() }),

    // Created at boot, not on first use: `beforeinstallprompt` fires once, early, and a listener
    // attached when the install banner first renders would miss it. See core/pwa/install.service.ts.
    provideAppInitializer(() => {
      inject(InstallService);
    }),
  ],
};
