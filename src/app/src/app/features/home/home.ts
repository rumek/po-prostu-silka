import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

/**
 * The approved member's landing screen.
 *
 * STILL A PLACEHOLDER, but no longer a promise: it used to say the schedule would appear "wkrótce",
 * and as of S-08 both member screens exist and carry nav entries, so it points at them instead. The
 * dashboard proper — the nearest upcoming classes as a card — is S-12.
 *
 * It exists because activeMemberGuard needs somewhere to admit an approved member to, and because
 * "reaches the app proper" is S-01's stated outcome.
 */
@Component({
  imports: [RouterLink],
  selector: 'app-home',
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
  protected readonly auth = inject(AuthService);
}
