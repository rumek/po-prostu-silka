import { Component } from '@angular/core';

/**
 * Route stubs, not UI.
 *
 * F-02 ships auth plumbing with no screens - the login and registration screens are S-01's stated
 * outcome. But the router needs a target for the guard's redirect and something to protect, or the
 * guard and interceptor would be dead code that nothing exercises.
 *
 * S-01 REPLACES BOTH of these with real components. Do not build on them.
 */

@Component({
  selector: 'app-login-placeholder',
  template: `<p>Login lands here. The screen arrives with S-01.</p>`,
})
export class LoginPlaceholder {}

@Component({
  selector: 'app-home-placeholder',
  template: `<p>Authenticated area. Real screens arrive with S-01 onward.</p>`,
})
export class HomePlaceholder {}
