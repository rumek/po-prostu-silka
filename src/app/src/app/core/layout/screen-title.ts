import { EffectRef, Injectable, computed, effect, inject, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRouteSnapshot, RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { ScreenLevel, leafOf, levelOf, parentOf } from './screen';

export const APP_NAME = 'Po Prostu Siłka';

/**
 * What the current screen is called and how deep it sits (mobile-native-feel).
 *
 * The route table is the source: every route declares a `title` and a `data.level`, and
 * `ScreenTitleStrategy` below publishes them here on each navigation. A screen whose name depends on
 * what it loaded — an exercise, a member's plan — overrides the title with `set()`; the override is
 * dropped on the next navigation, so it can never outlive the screen that made it.
 *
 * `document.title` follows the same value, so the browser tab, the task switcher and the phone's
 * app bar all say one thing.
 */
@Injectable({ providedIn: 'root' })
export class ScreenTitle {
  private readonly documentTitle = inject(Title);

  private readonly routeTitle = signal('');
  private readonly override = signal<string | null>(null);
  private readonly currentLevel = signal<ScreenLevel>('brand');
  private readonly currentParent = signal<string | null>(null);

  readonly title = computed(() => this.override() ?? this.routeTitle());
  readonly level = this.currentLevel.asReadonly();
  readonly parent = this.currentParent.asReadonly();

  /**
   * The strategy's entry point: a navigation resolved to this screen. `sameScreen` — only the query
   * changed — keeps the override: the screen is still showing the data that named it, and its
   * `useScreenTitle` effect will not re-run to name it again.
   */
  resolve(title: string, level: ScreenLevel, parent: string | null, sameScreen = false): void {
    this.routeTitle.set(title);
    if (!sameScreen) {
      this.override.set(null);
    }
    this.currentLevel.set(level);
    this.currentParent.set(parent);
    this.writeDocumentTitle();
  }

  /** A data-dependent name for the screen now showing. Lasts until the next navigation. */
  set(title: string): void {
    this.override.set(title);
    this.writeDocumentTitle();
  }

  // A brand screen is the app itself, so it carries the app's name alone.
  private writeDocumentTitle(): void {
    const title = this.title();
    this.documentTitle.setTitle(
      this.currentLevel() === 'brand' || !title ? APP_NAME : `${title} · ${APP_NAME}`,
    );
  }
}

/**
 * The router's hook. Called synchronously right after the new screen's component is created and
 * before any change detection runs, so a screen's own `useScreenTitle` effect always lands AFTER
 * the reset here — never under it.
 */
@Injectable({ providedIn: 'root' })
export class ScreenTitleStrategy extends TitleStrategy {
  private readonly screen = inject(ScreenTitle);
  private previous: ActivatedRouteSnapshot | null = null;

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const leaf = leafOf(snapshot.root);
    const sameScreen =
      this.previous !== null &&
      this.previous.routeConfig === leaf.routeConfig &&
      JSON.stringify(this.previous.params) === JSON.stringify(leaf.params);
    this.previous = leaf;

    this.screen.resolve(this.buildTitle(snapshot) ?? '', levelOf(leaf), parentOf(leaf), sameScreen);
  }
}

/**
 * For a screen whose title depends on its data: call as a field initialiser with the same
 * expression the screen's `h1` renders. `null` (still loading, not found) keeps the route's title.
 * Returns the effect so the caller can hold it in a field; it is destroyed with the component.
 */
export function useScreenTitle(title: () => string | null): EffectRef {
  const screen = inject(ScreenTitle);
  return effect(() => {
    const value = title();
    if (value) {
      screen.set(value);
    }
  });
}
