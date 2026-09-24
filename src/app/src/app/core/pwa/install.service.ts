import { DOCUMENT, Injectable, computed, inject, signal } from '@angular/core';

/**
 * Chromium's install event. Not in lib.dom.d.ts because it is not a standard — Safari and Firefox
 * never fire it, which is exactly why every caller must treat "no event" as a normal outcome.
 */
interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>;
  readonly userChoice: Promise<{ outcome: 'accepted' | 'dismissed'; platform: string }>;
}

/**
 * Holds the browser's deferred install prompt, so the app can offer "Zainstaluj" at a moment of its
 * choosing instead of Chrome's mini-infobar at a moment of Chrome's.
 *
 * <p>WHY IT STARTS AT BOOT. `beforeinstallprompt` fires once, early, and is not replayed. A service
 * created lazily — by the banner, which renders only after the session resolves — would attach its
 * listener after the event has already gone. `app.config.ts` instantiates it in an app initializer
 * for that reason alone.</p>
 *
 * <p>Everything degrades silently, like push: Safari, Firefox, an already-installed app and a server
 * render all simply never get an event, and `canInstall` stays false. iOS gets `needsManualInstall`
 * instead, since there the member has to install by hand.</p>
 */
@Injectable({ providedIn: 'root' })
export class InstallService {
  private readonly window = inject(DOCUMENT).defaultView;

  private readonly deferred = signal<BeforeInstallPromptEvent | null>(null);

  readonly canInstall = computed(() => this.deferred() !== null);

  /**
   * True on an iPhone or iPad that is not already running the installed app. iOS never fires
   * `beforeinstallprompt` and no script can open its share sheet, so the only thing the app can do
   * there is tell the member where "Do ekranu początkowego" lives. Worth doing: iOS delivers Web Push
   * only to an installed PWA, so an iPhone member who never installs never gets a notification.
   */
  readonly needsManualInstall = signal(false);

  constructor() {
    // Null during server-side rendering; there is no browser to install into.
    if (!this.window) {
      return;
    }

    this.window.addEventListener('beforeinstallprompt', (event) => {
      // Suppresses Chrome's own mini-infobar. The event is kept and replayed from our banner.
      event.preventDefault();
      this.deferred.set(event as BeforeInstallPromptEvent);
    });

    // Installed by any route — our banner, the browser menu, the address-bar icon. Whichever it
    // was, the stored event is spent and offering it again would do nothing.
    this.window.addEventListener('appinstalled', () => this.deferred.set(null));

    this.needsManualInstall.set(isIos(this.window.navigator) && !isStandalone(this.window));
  }

  /**
   * Shows the browser's install dialog. True when the member accepted.
   *
   * The event is single-use — `prompt()` throws on a second call — so it is dropped whatever the
   * answer. Chrome fires a fresh `beforeinstallprompt` later if the member declined, and the
   * listener above picks that one up.
   */
  async install(): Promise<boolean> {
    const event = this.deferred();

    if (event === null) {
      return false;
    }

    this.deferred.set(null);

    try {
      await event.prompt();
      const { outcome } = await event.userChoice;

      return outcome === 'accepted';
    } catch {
      return false;
    }
  }
}

/**
 * iPadOS 13+ reports itself as "Macintosh" in the user agent, so a touch-capable Mac is an iPad —
 * no real Mac has a touch screen.
 */
function isIos(navigator: Navigator): boolean {
  return (
    /iPad|iPhone|iPod/.test(navigator.userAgent) ||
    (/Macintosh/.test(navigator.userAgent) && navigator.maxTouchPoints > 1)
  );
}

/** `navigator.standalone` is Safari's own flag; the media query covers everything else. */
function isStandalone(window: Window): boolean {
  return (
    (window.navigator as Navigator & { standalone?: boolean }).standalone === true ||
    window.matchMedia?.('(display-mode: standalone)').matches === true
  );
}
