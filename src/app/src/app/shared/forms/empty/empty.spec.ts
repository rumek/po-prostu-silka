import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { provideRouter } from '@angular/router';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Empty } from './empty';

/** /my-classes' shape: the empty state a new member always sees first, and it leads somewhere. */
@Component({
  imports: [Empty, RouterLink],
  template: `
    <app-empty>
      Nie masz jeszcze żadnych zapisów.
      <a routerLink="/schedule">Zobacz grafik zajęć</a>
    </app-empty>
  `,
})
class Host {}

describe('Empty', () => {
  let fixture: ComponentFixture<Host>;

  function compiled(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [Host], providers: [provideRouter([])] });
    fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('carries the .empty class the global stylesheet styles', () => {
    expect(compiled().querySelector('p.empty')).not.toBeNull();
  });

  /**
   * THE REASON THIS PROJECTS WHERE app-loading FIXES. No two empty states in the app say the same
   * thing, and this one has to carry a link — "nothing here" with no next step is the one version
   * of the screen a new member will always see first.
   */
  it('projects whatever the screen has to say, links included', () => {
    const paragraph = compiled().querySelector('p.empty');

    expect(paragraph?.textContent).toContain('Nie masz jeszcze żadnych zapisów.');
    expect(paragraph?.querySelector('a')?.getAttribute('href')).toBe('/schedule');
  });

  /** Not an error, and it must not be announced as one — the reason it is not an `.alert`. */
  it('claims no alert role', () => {
    expect(compiled().querySelector('[role=alert]')).toBeNull();
  });
});
