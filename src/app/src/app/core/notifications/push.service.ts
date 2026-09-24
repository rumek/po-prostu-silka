import { HttpClient } from '@angular/common/http';
import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { SwPush } from '@angular/service-worker';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../auth/auth.service';

interface VapidKeyResponse {
  publicKey: string;
}

/**
 * Registers this browser for Web Push.
 *
 * Everything here degrades silently. Push is best-effort by design — email is the channel the
 * "no missed cancellations" guardrail rests on — so a browser that cannot or will not subscribe is
 * a normal outcome, not an error to surface. Reasons it legitimately fails:
 *
 *  - desktop Safari and any non-installed iPhone (iOS needs 16.4+ AND a home-screen install)
 *  - the member declined the notification permission prompt
 *  - the service worker is disabled (it is off in dev builds)
 *  - the server has no VAPID keys configured, which answers /vapid-key with 503
 */
@Injectable({ providedIn: 'root' })
export class PushService {
  private readonly http = inject(HttpClient);
  private readonly swPush = inject(SwPush);
  private readonly auth = inject(AuthService);

  private readonly subscribed = signal(false);
  private readonly unavailableReason = signal<string | null>(null);
  private readonly checked = signal(false);

  readonly isSubscribed = this.subscribed.asReadonly();

  /**
   * False until the browser's EXISTING registration has been read. A caller that offers to enable
   * push must wait for this, or it flashes the offer at a member who subscribed months ago.
   */
  readonly isReady = this.checked.asReadonly();

  constructor() {
    // Guarded on isEnabled because SwPush.subscription never emits when the service worker is off
    // (it is off in dev builds), which would leave `isReady` pending forever.
    if (!this.swPush.isEnabled) {
      this.checked.set(true);
      return;
    }

    // Re-attached on every account that signs in, not once per page load: this service is a root
    // singleton, and signing out and in as someone else does not reload the app.
    const account = computed(() => this.auth.user()?.id ?? null);

    effect(() => {
      if (account() !== null) {
        untracked(() => void this.attach());
      }
    });
  }

  /**
   * Seeds `subscribed` from the browser's registration and re-sends it to the API for the account
   * now signed in.
   *
   * <p>THE RE-SEND IS THE POINT. The browser's registration outlives both the session and the
   * server's row for it: a test-data reset deletes every row, the worker deletes one the push
   * service reported gone, and the row stays bound to whichever account subscribed first. A browser
   * that only read its own registration would call itself subscribed and receive nothing — or
   * receive the previous account's messages. Subscribe is an upsert on the endpoint that rebinds
   * it to the caller, so sending it again is always safe.</p>
   */
  private async attach(): Promise<void> {
    try {
      const subscription = await firstValueFrom(this.swPush.subscription);
      this.subscribed.set(subscription !== null);

      if (subscription !== null) {
        await this.post('/api/push/subscribe', subscription);
      }
    } catch {
      // Nothing registered, the browser refused to say, or the API was unreachable. Push is
      // best-effort; the next sign-in tries again.
    } finally {
      this.checked.set(true);
    }
  }

  /** Null when push is usable; a short reason string when it is not. For a later slice to render. */
  readonly unavailable = this.unavailableReason.asReadonly();

  readonly isSupported = computed(() => this.swPush.isEnabled);

  /**
   * Idempotent. The browser re-issues the same endpoint on re-subscribe and the API upserts on it,
   * so calling this repeatedly does not create duplicate subscriptions.
   */
  async subscribe(): Promise<boolean> {
    if (!this.swPush.isEnabled) {
      this.unavailableReason.set('service_worker_unavailable');
      return false;
    }

    try {
      const { publicKey } = await firstValueFrom(
        this.http.get<VapidKeyResponse>('/api/push/vapid-key'),
      );

      const subscription = await this.swPush.requestSubscription({
        serverPublicKey: publicKey,
      });

      await this.post('/api/push/subscribe', subscription);

      this.subscribed.set(true);
      this.unavailableReason.set(null);
      return true;
    } catch {
      // Declined permission, an unconfigured server, or an unsupported browser all land here and
      // all mean the same thing: this device will rely on email.
      this.unavailableReason.set('subscription_failed');
      this.subscribed.set(false);
      return false;
    }
  }

  /**
   * Called on sign-out, BEFORE the session ends — the API needs the cookie to know whose row to
   * delete. Unregisters the browser as well as the server row, so the next account to sign in on
   * this device is asked rather than silently handed the previous one's notifications. The browser
   * permission survives, so saying yes again costs one tap and no system dialog.
   */
  async unsubscribe(): Promise<void> {
    if (!this.swPush.isEnabled) {
      return;
    }

    try {
      const subscription = await firstValueFrom(this.swPush.subscription);
      if (!subscription) {
        return;
      }

      await this.post('/api/push/unsubscribe', subscription);
      await this.swPush.unsubscribe();
      this.subscribed.set(false);
    } catch {
      // Already gone, or the browser refused. Either way there is nothing to recover.
      this.subscribed.set(false);
    }
  }

  /** toJSON() gives the endpoint and the two client keys in the shape the API expects. */
  private post(url: string, subscription: PushSubscription): Promise<void> {
    const payload = subscription.toJSON() as {
      endpoint?: string;
      keys?: { p256dh?: string; auth?: string };
    };

    return firstValueFrom(
      this.http.post<void>(url, {
        endpoint: payload.endpoint,
        p256dh: payload.keys?.p256dh,
        auth: payload.keys?.auth,
      }),
    );
  }
}
