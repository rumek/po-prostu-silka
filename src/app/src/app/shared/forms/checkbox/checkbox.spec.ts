import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Checkbox } from './checkbox';

/** Signals, not plain fields: the app is zoneless and a reassigned property marks nothing dirty. */
@Component({
  imports: [Checkbox],
  template: `
    <app-checkbox [checked]="showInactive()" (checkedChange)="toggle()">
      Pokaż nieaktywne
    </app-checkbox>
  `,
})
class Host {
  readonly showInactive = signal(false);
  readonly toggles = signal(0);

  toggle(): void {
    this.toggles.update((count) => count + 1);
    this.showInactive.update((on) => !on);
  }
}

describe('Checkbox', () => {
  let fixture: ComponentFixture<Host>;

  function compiled(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function box(): HTMLInputElement {
    return compiled().querySelector('input[type=checkbox]') as HTMLInputElement;
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [Host] });
    fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    fixture.detectChanges();
  });

  /**
   * THE REASON THIS EXISTS. Both copies it replaces were the only controls in the app with no
   * shared class — a bare input rendering in the browser's default blue, beside text that had to be
   * associated with it by hand on every screen that wanted one.
   */
  it('wraps the box and its words in one label, so the association cannot be broken', () => {
    const label = compiled().querySelector('label.checkbox');

    expect(label).not.toBeNull();
    expect(label?.querySelector('input[type=checkbox]')).not.toBeNull();
    expect(label?.textContent?.trim()).toBe('Pokaż nieaktywne');
  });

  it('shows the state the caller holds', async () => {
    expect(box().checked).toBe(false);

    fixture.componentInstance.showInactive.set(true);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(box().checked).toBe(true);
  });

  it('tells the caller it changed, and writes nothing itself', async () => {
    box().click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance.toggles()).toBe(1);
    expect(fixture.componentInstance.showInactive()).toBe(true);
  });

  /** Clicking the words is clicking the box — what the stylesheet's `cursor: pointer` promises. */
  it('toggles when the label text is clicked', async () => {
    (compiled().querySelector('label.checkbox') as HTMLLabelElement).click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance.toggles()).toBe(1);
  });
});
