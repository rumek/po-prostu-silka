import { TestBed } from '@angular/core/testing';
import { InstallService } from './install.service';

/** A stand-in for Chromium's non-standard event; jsdom has no such type. */
function installEvent(outcome: 'accepted' | 'dismissed') {
  const event = new Event('beforeinstallprompt', { cancelable: true });

  return Object.assign(event, {
    prompt: vi.fn().mockResolvedValue(undefined),
    userChoice: Promise.resolve({ outcome, platform: 'web' }),
  });
}

describe('InstallService', () => {
  function service(): InstallService {
    return TestBed.inject(InstallService);
  }

  it('cannot install until the browser offers it', () => {
    expect(service().canInstall()).toBe(false);
  });

  // preventDefault is what suppresses Chrome's mini-infobar; without it the member gets two offers.
  it('holds the event back and suppresses the browser infobar', () => {
    const installer = service();
    const event = installEvent('accepted');

    window.dispatchEvent(event);

    expect(event.defaultPrevented).toBe(true);
    expect(installer.canInstall()).toBe(true);
  });

  it('replays the held event and reports the answer', async () => {
    const installer = service();
    const event = installEvent('accepted');
    window.dispatchEvent(event);

    await expect(installer.install()).resolves.toBe(true);
    expect(event.prompt).toHaveBeenCalledOnce();
  });

  // prompt() is single-use; offering the spent event again would throw.
  it('drops the event whatever the answer', async () => {
    const installer = service();
    window.dispatchEvent(installEvent('dismissed'));

    await expect(installer.install()).resolves.toBe(false);
    expect(installer.canInstall()).toBe(false);
  });

  it('withdraws once the app is installed some other way', () => {
    const installer = service();
    window.dispatchEvent(installEvent('accepted'));

    window.dispatchEvent(new Event('appinstalled'));

    expect(installer.canInstall()).toBe(false);
  });

  describe('on iOS', () => {
    // jsdom has no maxTouchPoints or standalone, so they are defined outright rather than spied on.
    function stubNavigator(userAgent: string, maxTouchPoints: number, standalone?: boolean): void {
      vi.spyOn(navigator, 'userAgent', 'get').mockReturnValue(userAgent);
      Object.defineProperty(navigator, 'maxTouchPoints', {
        value: maxTouchPoints,
        configurable: true,
      });
      Object.defineProperty(navigator, 'standalone', { value: standalone, configurable: true });
    }

    afterEach(() => {
      vi.restoreAllMocks();
      Reflect.deleteProperty(navigator, 'maxTouchPoints');
      Reflect.deleteProperty(navigator, 'standalone');
    });

    it('asks for a manual install in Safari on an iPhone', () => {
      stubNavigator('Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)', 5, false);

      expect(service().needsManualInstall()).toBe(true);
    });

    // iPadOS 13+ claims to be a Mac; the touch screen is what gives it away.
    it('recognises an iPad that reports itself as a Mac', () => {
      stubNavigator('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', 5, false);

      expect(service().needsManualInstall()).toBe(true);
    });

    it('does not ask inside the installed app', () => {
      stubNavigator('Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)', 5, true);

      expect(service().needsManualInstall()).toBe(false);
    });

    it('does not ask on a real Mac', () => {
      stubNavigator('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', 0);

      expect(service().needsManualInstall()).toBe(false);
    });
  });
});
