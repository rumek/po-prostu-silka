import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
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
  ],
};
