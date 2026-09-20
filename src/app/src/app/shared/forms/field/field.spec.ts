import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Field } from './field';

/**
 * A host, because projection is the whole surface and there is no way to exercise it from the
 * component alone. Signals rather than plain fields: the app runs zoneless, so a bare property
 * reassigned in a test never marks the host dirty and the DOM read back is stale.
 */
@Component({
  imports: [Field],
  template: `
    <app-field [label]="label()" [for]="'email'">
      <input id="email" [attr.aria-invalid]="invalid()" />
      @if (invalid()) {
        <p class="field-error">Podaj poprawny adres e-mail.</p>
      }
    </app-field>
  `,
})
class Host {
  readonly label = signal<string | undefined>('Adres e-mail');
  readonly invalid = signal(false);
}

/** The plan builder's shape: an element for a label, because the value is no longer editable. */
@Component({
  imports: [Field],
  template: `
    <app-field>
      <span slot="label" class="builder-caption">Członek</span>
      <p class="builder-member">Anna Kowalska</p>
    </app-field>
  `,
})
class ProjectedLabelHost {}

describe('Field', () => {
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

  it('carries the .field block the global stylesheet styles', () => {
    expect(compiled().querySelector('div.field')).not.toBeNull();
  });

  /**
   * THE PAIR MOST EASILY FORGOTTEN WHEN WRITTEN BY HAND, which is why the component emits it rather
   * than asking the caller to.
   */
  it('labels the control it was told to label', () => {
    const label = compiled().querySelector('label');

    expect(label?.textContent?.trim()).toBe('Adres e-mail');
    expect(label?.getAttribute('for')).toBe('email');
  });

  it('projects the control into the block', () => {
    expect(compiled().querySelector('div.field > input#email')).not.toBeNull();
  });

  /**
   * THE REASON THIS PROJECTS RATHER THAN OWNS. The component has no say in when an error shows or
   * what it says — profile's password fields choose between a server sentence and a fallback, and
   * the plan builder shows two errors from two unrelated sources.
   */
  it('projects whatever the screen says about the control, whenever it says it', async () => {
    expect(compiled().querySelector('.field-error')).toBeNull();

    fixture.componentInstance.invalid.set(true);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(compiled().querySelector('.field-error')?.textContent?.trim()).toBe(
      'Podaj poprawny adres e-mail.',
    );
    expect(compiled().querySelector('input')?.getAttribute('aria-invalid')).toBe('true');
  });

  it('renders no label element at all when it was given no words', async () => {
    fixture.componentInstance.label.set(undefined);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(compiled().querySelector('label')).toBeNull();
    expect(compiled().querySelector('input#email')).not.toBeNull();
  });

  /**
   * The plan builder's case: a <label> pointing at static text is a lie to a screen reader, so that
   * screen supplies its own element instead of a word.
   */
  it('accepts an element in place of a label', async () => {
    const projected = TestBed.createComponent(ProjectedLabelHost);
    await projected.whenStable();
    projected.detectChanges();

    const host = projected.nativeElement as HTMLElement;

    expect(host.querySelector('label')).toBeNull();
    expect(host.querySelector('div.field > .builder-caption')?.textContent?.trim()).toBe('Członek');
    expect(host.querySelector('div.field > .builder-member')).not.toBeNull();
  });
});
