import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReadonlyField } from './readonly-field';

/** A host, because the inputs are the whole surface and setting them through one is how callers do. */
@Component({
  imports: [ReadonlyField],
  template: `<app-readonly-field [label]="label()" [value]="value()" />`,
})
class Host {
  // Signals, not plain fields: the app runs zoneless, so a bare property reassigned in a test never
  // marks the host dirty and the rendered value silently stays stale.
  readonly label = signal('Adres e-mail');
  readonly value = signal<string | null | undefined>('anna@test.local');
}

describe('ReadonlyField', () => {
  let fixture: ComponentFixture<Host>;

  function compiled(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [Host] });
    fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('renders the label and the value as a description pair', () => {
    expect(compiled().querySelector('dt')?.textContent?.trim()).toBe('Adres e-mail');
    expect(compiled().querySelector('dd')?.textContent?.trim()).toBe('anna@test.local');
  });

  /**
   * THE REASON THIS EXISTS. A disabled input still reads as something that might become editable,
   * and every value this renders is one no screen in the app can change.
   */
  it('renders no form control of any kind', () => {
    expect(compiled().querySelector('input')).toBeNull();
    expect(compiled().querySelector('textarea')).toBeNull();
    expect(compiled().querySelector('select')).toBeNull();
  });

  /**
   * Self-contained: it brings its own list rather than projecting a pair into the caller's, so it
   * stays valid markup wherever it is dropped — /register puts it inside a form, /profile beside one.
   */
  it('carries its own description list', () => {
    const list = compiled().querySelector('dl.readonly-field');

    expect(list).not.toBeNull();
    expect(list?.querySelectorAll('dt')).toHaveLength(1);
    expect(list?.querySelectorAll('dd')).toHaveLength(1);
  });

  /** /profile reads these from a session that may still be loading; an empty line beats a crash. */
  it('renders an empty value rather than refusing, when there is nothing yet', async () => {
    fixture.componentInstance.value.set(null);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(compiled().querySelector('dd')?.textContent?.trim()).toBe('');
    expect(compiled().querySelector('dt')?.textContent?.trim()).toBe('Adres e-mail');
  });
});
