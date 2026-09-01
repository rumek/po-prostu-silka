import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { SwPush } from '@angular/service-worker';
import { firstValueFrom } from 'rxjs';

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

  private readonly subscribed = signal(false);
  private readonly unavailableReason = signal<string | null>(null);

  readonly isSubscribed = this.subscribed.asReadonly();

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

      // toJSON() gives the endpoint and the two client keys in the shape the API expects.
      const payload = subscription.toJSON() as {
        endpoint?: string;
        keys?: { p256dh?: string; auth?: string };
      };

      await firstValueFrom(
        this.http.post<void>('/api/push/subscribe', {
          endpoint: payload.endpoint,
          p256dh: payload.keys?.p256dh,
          auth: payload.keys?.auth,
        }),
      );

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

  async unsubscribe(): Promise<void> {
    if (!this.swPush.isEnabled) {
      return;
    }

    try {
      const subscription = await firstValueFrom(this.swPush.subscription);
      if (!subscription) {
        return;
      }

      const payload = subscription.toJSON() as { endpoint?: string };

      await firstValueFrom(
        this.http.post<void>('/api/push/unsubscribe', {
          endpoint: payload.endpoint,
          p256dh: '',
          auth: '',
        }),
      );

      await this.swPush.unsubscribe();
      this.subscribed.set(false);
    } catch {
      // Already gone, or the browser refused. Either way there is nothing to recover.
      this.subscribed.set(false);
    }
  }
}
