import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SwPush } from '@angular/service-worker';
import { of } from 'rxjs';
import { CurrentUser } from '../auth/auth.models';
import { AuthService } from '../auth/auth.service';
import { PushService } from './push.service';

/**
 * The behaviour that matters here is DEGRADATION. Push is best-effort — email carries the
 * guarantee — so every failure path must leave the app usable rather than surfacing an error.
 */
describe('PushService', () => {
  const VAPID_KEY = 'BBAUIQCQOzJpuRcUFb9MTPBsb1kVbC8RsACWEb6ApDk';

  /** The signed-in account. PushService re-attaches the browser's registration whenever it changes. */
  const account = signal<CurrentUser | null>(null);

  function configure(swPush: Partial<SwPush>) {
    account.set(null);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SwPush, useValue: swPush },
        { provide: AuthService, useValue: { user: account.asReadonly() } },
      ],
    });
  }

  function signIn(id: string) {
    account.set({ id, email: `${id}@test.local`, displayName: id, status: 'Active', roles: [] });
    TestBed.tick();
  }

  function fakeSubscription(endpoint: string) {
    return {
      toJSON: () => ({ endpoint, keys: { p256dh: 'p256dh-value', auth: 'auth-value' } }),
    } as unknown as PushSubscription;
  }

  it('no-ops when the service worker is unavailable', async () => {
    configure({ isEnabled: false });
    const service = TestBed.inject(PushService);
    const controller = TestBed.inject(HttpTestingController);

    const result = await service.subscribe();

    expect(result).toBe(false);
    expect(service.unavailable()).toBe('service_worker_unavailable');

    // Nothing should have been requested — an unsupported browser must not hit the API at all.
    controller.verify();
  });

  it('reports unsupported without throwing', () => {
    configure({ isEnabled: false });
    const service = TestBed.inject(PushService);

    expect(service.isSupported()).toBe(false);
    expect(() => service.isSubscribed()).not.toThrow();
  });

  it('fetches the VAPID key and posts the subscription', async () => {
    configure({
      isEnabled: true,
      requestSubscription: vi.fn().mockResolvedValue(fakeSubscription('https://push.test/abc')),
    });
    const service = TestBed.inject(PushService);
    const controller = TestBed.inject(HttpTestingController);

    const pending = service.subscribe();

    controller.expectOne('/api/push/vapid-key').flush({ publicKey: VAPID_KEY });

    // requestSubscription() is a promise, so the POST is only issued once its microtask drains.
    const post = await vi.waitFor(() => controller.expectOne('/api/push/subscribe'));
    expect(post.request.body.endpoint).toBe('https://push.test/abc');
    expect(post.request.body.p256dh).toBe('p256dh-value');
    post.flush(null);

    await expect(pending).resolves.toBe(true);
    expect(service.isSubscribed()).toBe(true);
    controller.verify();
  });

  it('degrades silently when the member denies permission', async () => {
    configure({
      isEnabled: true,
      requestSubscription: vi.fn().mockRejectedValue(new Error('permission denied')),
    });
    const service = TestBed.inject(PushService);
    const controller = TestBed.inject(HttpTestingController);

    const pending = service.subscribe();
    controller.expectOne('/api/push/vapid-key').flush({ publicKey: VAPID_KEY });

    // Resolves false rather than rejecting — a declined prompt is a normal outcome.
    await expect(pending).resolves.toBe(false);
    expect(service.isSubscribed()).toBe(false);
    expect(service.unavailable()).toBe('subscription_failed');
  });

  it('degrades silently when the server has no VAPID keys configured', async () => {
    configure({ isEnabled: true, requestSubscription: vi.fn() });
    const service = TestBed.inject(PushService);
    const controller = TestBed.inject(HttpTestingController);

    const pending = service.subscribe();
    controller
      .expectOne('/api/push/vapid-key')
      .flush(null, { status: 503, statusText: 'Service Unavailable' });

    await expect(pending).resolves.toBe(false);
    expect(service.unavailable()).toBe('subscription_failed');
  });

  // The browser's registration outlives the server's row for it — a test-data reset deletes every
  // row — and the row stays bound to whoever subscribed first. Reading it back without re-sending it
  // left a device that called itself subscribed and received nothing.
  it('re-sends an existing registration for each account that signs in', async () => {
    configure({ isEnabled: true, subscription: of(fakeSubscription('https://push.test/abc')) });
    const service = TestBed.inject(PushService);
    const controller = TestBed.inject(HttpTestingController);

    signIn('member-1');
    const first = await vi.waitFor(() => controller.expectOne('/api/push/subscribe'));
    expect(first.request.body.endpoint).toBe('https://push.test/abc');
    first.flush(null);

    await vi.waitFor(() => expect(service.isReady()).toBe(true));
    expect(service.isSubscribed()).toBe(true);

    // Signing in as someone else does not reload the app; the device must move to them.
    signIn('member-2');
    (await vi.waitFor(() => controller.expectOne('/api/push/subscribe'))).flush(null);
    controller.verify();
  });

  it('sends nothing on sign-in when this browser never subscribed', async () => {
    configure({ isEnabled: true, subscription: of(null) });
    const service = TestBed.inject(PushService);
    const controller = TestBed.inject(HttpTestingController);

    signIn('member-1');

    await vi.waitFor(() => expect(service.isReady()).toBe(true));
    expect(service.isSubscribed()).toBe(false);
    controller.verify();
  });

  // On sign-out the device leaves the account, server row AND browser registration, so the next
  // person to sign in here is asked rather than handed the previous account's notifications.
  it('removes the server row and the browser registration on unsubscribe', async () => {
    const unsubscribe = vi.fn().mockResolvedValue(undefined);
    configure({
      isEnabled: true,
      subscription: of(fakeSubscription('https://push.test/abc')),
      unsubscribe,
    });
    const service = TestBed.inject(PushService);
    const controller = TestBed.inject(HttpTestingController);

    const pending = service.unsubscribe();

    const post = await vi.waitFor(() => controller.expectOne('/api/push/unsubscribe'));
    expect(post.request.body.endpoint).toBe('https://push.test/abc');
    post.flush(null);

    await pending;
    expect(unsubscribe).toHaveBeenCalled();
    expect(service.isSubscribed()).toBe(false);
  });
});
