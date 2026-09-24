import { Component, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Select } from './select';

/**
 * A host with a real reactive form, because the claim under test is that a projected select keeps
 * binding in the CALLER's context — which a host with a plain select could not show.
 */
@Component({
  imports: [Select, ReactiveFormsModule],
  template: `
    <form [formGroup]="form">
      <app-select>
        <select id="classTypeId" formControlName="classTypeId" (change)="changed.set(true)">
          <option value="">Wybierz typ…</option>
          <option value="1">Joga</option>
        </select>
      </app-select>
    </form>
  `,
})
class Host {
  readonly form = new FormGroup({ classTypeId: new FormControl('') });
  readonly changed = signal(false);
}

describe('Select', () => {
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

  /**
   * THE REASON THIS EXISTS. A <select> is a replaced element that can hold nothing of ours — so a
   * select outside this wrapper has its native arrow suppressed by `appearance: none` and nothing
   * at all in its place.
   */
  it('puts the select inside the wrapper that carries the chevron', () => {
    expect(compiled().querySelector('span.select > select')).not.toBeNull();
  });

  /** The arrow is the icon set's, not a text glyph, so it matches every other icon's stroke. */
  it('draws the chevron from the icon set', () => {
    expect(compiled().querySelector('span.select > app-icon.select-chevron svg')).not.toBeNull();
  });

  it('projects the select unmodified, options and all', () => {
    const select = compiled().querySelector('select');

    expect(select?.id).toBe('classTypeId');
    expect(select?.querySelectorAll('option')).toHaveLength(2);
  });

  /**
   * Projected content is compiled in the caller's template, which is what lets this component take
   * no inputs: the form binding and the listener are the host's, not ours.
   */
  it('leaves the bindings of the calling template working', async () => {
    const select = compiled().querySelector('select') as HTMLSelectElement;

    select.value = '1';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance.form.controls.classTypeId.value).toBe('1');
    expect(fixture.componentInstance.changed()).toBe(true);
  });
});
