import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SwPush } from '@angular/service-worker';
import { PushService } from './push.service';

/**
 * The behaviour that matters here is DEGRADATION. Push is best-effort — email carries the
 * guarantee — so every failure path must leave the app usable rather than surfacing an error.
 */
describe('PushService', () => {
  const VAPID_KEY = 'BBAUIQCQOzJpuRcUFb9MTPBsb1kVbC8RsACWEb6ApDk';

  function configure(swPush: Partial<SwPush>) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SwPush, useValue: swPush },
      ],
    });
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
});
