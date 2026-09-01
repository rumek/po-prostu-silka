import { ApplicationConfig, isDevMode, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideServiceWorker } from '@angular/service-worker';
import { provideRouter } from '@angular/router';
import { provideClientHydration } from '@angular/platform-browser';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
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
  ],
};
