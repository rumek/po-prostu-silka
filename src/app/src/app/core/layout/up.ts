import { Location } from '@angular/common';
import { Directive, Injectable, inject, input } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, NavigationStart, Router } from '@angular/router';

interface Entry {
  /** The router's `navigationId` stored in this entry's `history.state` — how popstate names it. */
  readonly id: number;
  readonly url: string;
}

function pathOf(url: string): string {
  return url.split(/[?#]/)[0];
}

/**
 * Names this page load in every entry it records. The router's `navigationId` restarts at 1 on each
 * load while entries from before a reload keep their old ids, so an id alone could match an entry of
 * THIS load that merely shares the number — and the model would then pop into the unknown.
 */
const LOAD_ID = `${Date.now()}-${Math.random().toString(36).slice(2)}`;

interface EntryState {
  navigationId?: number;
  upLoad?: string;
}

/**
 * "Up", the way a native app does it (mobile-native-feel): back to a screen that is already in the
 * history is a POP, so the screen left behind does not linger as a forward step for Android's back
 * gesture; only when it is not there — a deep link, a reload, a push notification — is it a
 * navigation, and then a REPLACE, so back from the parent still leaves rather than returning.
 *
 * The browser does not expose its history, so this keeps a model of this session's part of it from
 * the router's events: a push appends, a replace overwrites, a popstate moves to the entry whose
 * `navigationId` the router restored. Each entry is also tagged with this page load's `LOAD_ID`.
 * Anything it cannot place — the entries before the app loaded, an entry from before a reload —
 * resets the model to the current screen alone, which only ever makes `to()` fall back to the
 * replace.
 *
 * An overlay's same-URL entry (shared/forms/overlay-history.ts) is invisible here: the router skips
 * same-URL popstates, so it produces no events. The overlay pops its own entry when it closes, so
 * normally there is nothing to count.
 *
 * KNOWN LIMIT: an overlay torn down by a navigation — one started from inside it (the admin
 * actions overlay's "Edytuj") or from outside (an auth redirect) — cannot pop, and its entry stays
 * under the new screen. A `to()` that crosses such an entry lands one entry short. Today only the
 * desk calendar's overlays navigate, and the desk has neither the app bar's arrow nor the bottom
 * bar, so no caller of `to()` can meet one. Revisit this if a phone overlay ever navigates.
 */
@Injectable({ providedIn: 'root' })
export class Up {
  private readonly router = inject(Router);
  private readonly location = inject(Location);

  private entries: Entry[] = [];
  private index = -1;
  private readonly starts = new Map<number, NavigationStart>();

  constructor() {
    this.router.events.pipe(takeUntilDestroyed()).subscribe((event) => {
      if (event instanceof NavigationStart) {
        this.starts.set(event.id, event);
      } else if (event instanceof NavigationEnd) {
        this.record(event);
      }
    });
  }

  /** Go up to `target`: pop back to it if it is below this screen, else replace this screen with it. */
  to(target: string): void {
    const steps = this.stepsBackTo(target);

    if (steps > 0) {
      this.location.historyGo(-steps);
    } else {
      void this.router.navigateByUrl(target, { replaceUrl: true });
    }
  }

  /**
   * The app bar's back arrow: back to the screen this one was opened from, as Android's back does,
   * when that screen is in this session's history — a child can be reached from more than its
   * parent (the bar's profile icon opens Moje konto from any screen). Only with nothing to return
   * to — a deep link, a reload — does it fall back to `to(parent)`.
   */
  back(parent: string): void {
    if (this.index > 0) {
      this.location.historyGo(-1);
    } else {
      this.to(parent);
    }
  }

  /** How many entries back the nearest `target` sits in this session's history, or 0 if it is not. */
  stepsBackTo(target: string): number {
    const path = pathOf(target);

    for (let k = this.index - 1; k >= 0; k--) {
      if (pathOf(this.entries[k].url) === path) {
        return this.index - k;
      }
    }
    return 0;
  }

  private record(end: NavigationEnd): void {
    const start = this.starts.get(end.id);
    this.starts.clear();

    const extras = this.router.lastSuccessfulNavigation()?.extras;
    if (extras?.skipLocationChange) {
      return;
    }

    // What the router actually wrote into history.state, read back rather than assumed: a popstate
    // navigation rewrites its entry's id, and the next popstate names the entry by that.
    const state = (this.location.getState() as EntryState | null) ?? {};
    const entry: Entry = { id: state.navigationId ?? end.id, url: end.urlAfterRedirects };

    // Tag the entry with this load, keeping everything the router stored there.
    if (state.upLoad !== LOAD_ID) {
      this.location.replaceState(this.location.path(true), '', { ...state, upLoad: LOAD_ID });
    }

    if (start?.navigationTrigger === 'popstate') {
      const restored = start.restoredState as EntryState | null;
      const k =
        restored?.upLoad === LOAD_ID
          ? this.entries.findIndex((e) => e.id === restored.navigationId)
          : -1;

      if (k >= 0) {
        this.index = k;
        this.entries[k] = entry;
      } else {
        this.entries = [entry];
        this.index = 0;
      }
    } else if (extras?.replaceUrl && this.index >= 0) {
      this.entries[this.index] = entry;
    } else {
      this.entries = [...this.entries.slice(0, this.index + 1), entry];
      this.index++;
    }
  }
}

/**
 * A link up to a screen's parent — the desktop "Wróć…" links. A real href, so middle-click, a new
 * tab and a long press still work; a plain click goes through `Up.to` like the phone bar's arrow.
 */
@Directive({
  selector: 'a[appUp]',
  host: { '[attr.href]': 'appUp()', '(click)': 'onClick($event)' },
})
export class UpLink {
  private readonly up = inject(Up);

  readonly appUp = input.required<string>();

  protected onClick(event: MouseEvent): void {
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
      return;
    }
    event.preventDefault();
    this.up.to(this.appUp());
  }
}
