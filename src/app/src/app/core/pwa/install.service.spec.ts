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
});
