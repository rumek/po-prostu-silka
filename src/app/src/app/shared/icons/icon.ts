import { Component, input } from '@angular/core';

/** Every icon the app knows. Adding one means adding a case to the template, and nothing else. */
export type IconName = 'home' | 'calendar' | 'booking' | 'plan' | 'more';

/**
 * The app's icon primitive — the first one this codebase has had.
 *
 * The folder is `icons/`, plural, and must stay that way: `.gitignore:443` carries the stock macOS
 * `Icon` entry, which is meant for the `Icon\r\r` custom-folder-icon file but as written matches any
 * path segment named `Icon` — case-insensitively on Windows and macOS. A folder called `icon/` is
 * silently untracked here.
 *
 * INLINE SVG, NO LIBRARY, and that is a budget decision rather than a taste one. The initial bundle
 * sits at ~497 kB against the 500 kB warning in angular.json, and this renders inside the shell, which
 * is eager: any icon package large enough to be worth depending on would take the build past the
 * budget on the day it landed. Five hand-authored paths cost about a kilobyte.
 *
 * `currentColor` everywhere, and no colour of its own: an icon inherits from whatever it sits in, so
 * the active/inactive states of a tab are decided by the tab, not here.
 *
 * DECORATIVE BY DEFAULT. The svg is aria-hidden, so an icon never announces itself. Where an icon is
 * the ONLY content of a control — as in the bottom bar — the CONTROL carries the accessible name via
 * aria-label. An icon that labelled itself would announce twice everywhere else.
 */
@Component({
  selector: 'app-icon',
  styleUrl: './icon.scss',
  templateUrl: './icon.html',
})
export class Icon {
  readonly name = input.required<IconName>();
}
