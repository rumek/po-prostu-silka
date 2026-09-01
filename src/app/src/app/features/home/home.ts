import { Component, inject } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';

/**
 * The approved member's landing screen.
 *
 * A PLACEHOLDER, and knowingly so: S-03 replaces it with the class schedule. It exists because
 * activeMemberGuard needs somewhere to admit an approved member to, and because "reaches the app
 * proper" is S-01's stated outcome — it just is not much app yet.
 *
 * Note what this means for the ActiveMember policy: no production endpoint sits behind it until
 * S-03, so the claim-refresh behaviour the awaiting screen depends on is proved by the backend's
 * environment-guarded probe test, not by anything reachable from here.
 */
@Component({
  selector: 'app-home',
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
  protected readonly auth = inject(AuthService);
}
