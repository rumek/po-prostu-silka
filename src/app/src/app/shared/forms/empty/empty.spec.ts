import { Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { provideRouter } from '@angular/router';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Empty } from './empty';

/** /admin/classes/new's shape: the empty state that leads somewhere, with an icon and an action. */
@Component({
  imports: [Empty, RouterLink],
  template: `
    <app-empty icon="group" [compact]="compact()">
      Najpierw zdefiniuj grupę.
      <a slot="action" class="button" routerLink="/admin/class-groups">Przejdź do grup</a>
    </app-empty>
  `,
})
class Host {
  readonly compact = signal(false);
}

/** The bare shape: words and nothing else. */
@Component({
  imports: [Empty],
  template: `<app-empty>Plan jest pusty.</app-empty>`,
})
class BareHost {}

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

  /**
   * THE REASON THIS PROJECTS WHERE app-loading FIXES. No two empty states in the app say the same
   * thing, and some carry a next step.
   */
  it('projects whatever the screen has to say into the text', () => {
    expect(compiled().querySelector('.empty-text')?.textContent).toContain(
      'Najpierw zdefiniuj grupę.',
    );
  });

  it('puts the action below the words, not inside them', () => {
    const action = compiled().querySelector('.empty-action a');

    expect(action?.getAttribute('href')).toBe('/admin/class-groups');
    expect(compiled().querySelector('.empty-text a')).toBeNull();
  });

  it('draws the icon it is given', () => {
    expect(compiled().querySelector('.empty-icon app-icon')).not.toBeNull();
  });

  it('is a card of its own by default, and sits inside one when compact', async () => {
    expect(compiled().querySelector('.empty--compact')).toBeNull();

    fixture.componentInstance.compact.set(true);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(compiled().querySelector('.empty--compact')).not.toBeNull();
  });

  /** Not an error, and it must not be announced as one — the reason it is not an `.alert`. */
  it('claims no alert role', () => {
    expect(compiled().querySelector('[role=alert]')).toBeNull();
  });

  it('renders no icon and no action when given neither', async () => {
    const bare = TestBed.createComponent(BareHost);
    await bare.whenStable();
    bare.detectChanges();
    const element = bare.nativeElement as HTMLElement;

    expect(element.querySelector('.empty-icon')).toBeNull();
    expect(element.querySelector('.empty-action')?.childElementCount).toBe(0);
    expect(element.querySelector('.empty-text')?.textContent?.trim()).toBe('Plan jest pusty.');
  });
});
