import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ScheduledClass } from '../../../core/scheduling/class.models';
import { ClassActionsOverlay, bookedCount, canCancel } from './class-actions-overlay';

const JOGA: ScheduledClass = {
  id: 'c1',
  classTypeId: 't1',
  name: 'Joga',
  description: null,
  startsAt: new Date(Date.now() + 86_400_000).toISOString(),
  durationMinutes: 30,
  instructorMemberId: 'u1',
  instructor: 'Ola',
  capacity: 20,
  freeSpots: 20,
  status: 'Scheduled',
};

/** Somebody is signed up — the one difference that decides Odwołaj over Usuń. */
const BOOKED: ScheduledClass = { ...JOGA, freeSpots: 17 };

/** Hosts the overlay the way the admin screen does, so inputs and outputs are exercised as bound. */
@Component({
  imports: [ClassActionsOverlay],
  template: `
    <app-class-actions-overlay
      [row]="row()"
      [busy]="busy()"
      [failure]="failure()"
      [deleteBlocked]="deleteBlocked()"
      (duplicateRequested)="duplicates.push($event)"
      (deleteRequested)="deletes = deletes + 1"
      (cancelRequested)="cancels = cancels + 1"
      (bookingsRequested)="bookings = bookings + 1"
      (closed)="closes = closes + 1"
    />
  `,
})
class Host {
  readonly row = signal<ScheduledClass>(JOGA);
  readonly busy = signal(false);
  readonly failure = signal<string | null>(null);
  readonly deleteBlocked = signal(false);
  readonly duplicates: number[] = [];
  deletes = 0;
  cancels = 0;
  bookings = 0;
  closes = 0;
}

/**
 * Everything an admin does to one class (S-20). It renders and reports; the screen performs every
 * request, so none of these tests needs HTTP.
 */
describe('ClassActionsOverlay', () => {
  let fixture: ComponentFixture<Host>;
  let host: Host;

  function create(row: ScheduledClass = JOGA): void {
    TestBed.configureTestingModule({ imports: [Host], providers: [provideRouter([])] });
    fixture = TestBed.createComponent(Host);
    host = fixture.componentInstance;
    host.row.set(row);
    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(): string {
    return element().textContent ?? '';
  }

  function button(label: string): HTMLButtonElement | undefined {
    return [...element().querySelectorAll<HTMLButtonElement>('.overlay-panel button')].find((b) =>
      (b.textContent ?? '').includes(label),
    );
  }

  function press(label: string): void {
    button(label)!.click();
    fixture.detectChanges();
  }

  it('is a labelled dialog naming the class, its trainer and its spots', () => {
    create(BOOKED);

    const panel = element().querySelector('.overlay-panel')!;

    expect(panel.getAttribute('role')).toBe('dialog');
    expect(panel.getAttribute('aria-modal')).toBe('true');
    expect(element().querySelector(`#${panel.getAttribute('aria-labelledby')}`)?.textContent).toBe(
      'Joga',
    );
    expect(text()).toContain('Ola');
    expect(text()).toContain('17 / 20');
  });

  it('offers all four actions', () => {
    create();

    const edit = element().querySelector<HTMLAnchorElement>('.overlay-panel a')!;

    expect(edit.textContent).toContain('Edytuj');
    expect(edit.getAttribute('href')).toBe('/admin/classes/c1');
    expect(button('Powiel')).toBeDefined();
    expect(button('Zapisani')).toBeDefined();
    expect(button('Usuń')).toBeDefined();
  });

  // --- the one-button rule ---------------------------------------------------

  it('offers Usuń on an empty class and Odwołaj on a booked one, never both', () => {
    create();
    expect(button('Usuń')).toBeDefined();
    expect(button('Odwołaj')).toBeUndefined();

    host.row.set(BOOKED);
    fixture.detectChanges();

    expect(button('Odwołaj')).toBeDefined();
    expect(button('Usuń')).toBeUndefined();
  });

  it('keeps the rule in one place', () => {
    expect(canCancel(JOGA)).toBe(false);
    expect(canCancel(BOOKED)).toBe(true);
    // Already cancelled: cancelling again is refused with already_cancelled, so it offers Usuń.
    expect(canCancel({ ...BOOKED, status: 'Cancelled' })).toBe(false);
    expect(bookedCount(BOOKED)).toBe(3);
  });

  // --- the steps -------------------------------------------------------------

  it('confirms a duplicate with the number of weeks, and can go back', () => {
    create();

    press('Powiel');
    expect(text()).toContain('Powiel „Joga”');

    const weeks = element().querySelector<HTMLInputElement>('#class-actions-weeks')!;
    expect(element().querySelector('label[for="class-actions-weeks"]')).not.toBeNull();

    weeks.value = '2';
    weeks.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    press('Powiel');
    expect(host.duplicates).toEqual([2]);

    press('Anuluj');
    expect(button('Zapisani')).toBeDefined();
  });

  it('refuses an out-of-range week count under the field, without asking the server', () => {
    create();

    press('Powiel');
    const weeks = element().querySelector<HTMLInputElement>('#class-actions-weeks')!;
    weeks.value = '20';
    weeks.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    press('Powiel');

    expect(host.duplicates).toEqual([]);
    const error = element().querySelector('#class-actions-weeks-error');
    expect(error?.textContent).toContain('1–8');
    expect(weeks.getAttribute('aria-describedby')).toBe('class-actions-weeks-error');

    // Correcting the count clears the refusal and lets the duplicate through.
    weeks.value = '3';
    weeks.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(element().querySelector('#class-actions-weeks-error')).toBeNull();

    press('Powiel');
    expect(host.duplicates).toEqual([3]);
  });

  it('asks before deleting, and reports only on the confirmation', () => {
    create();

    press('Usuń');
    expect(text()).toContain('Usunąć „Joga”');
    expect(text()).toContain('na dobre?');
    expect(host.deletes).toBe(0);

    press('Tak, usuń');
    expect(host.deletes).toBe(1);
  });

  it('names how many people a cancellation will notify', () => {
    create(BOOKED);

    press('Odwołaj');

    expect(text()).toContain('Odwołać „Joga”');
    expect(text()).toContain('Powiadomimy 3 zapisanych osób');
    expect(text()).toContain('nie można cofnąć');
    expect(host.cancels).toBe(0);

    press('Tak, odwołaj');
    expect(host.cancels).toBe(1);

    press('Wróć');
    expect(button('Odwołaj')).toBeDefined();
  });

  it('says only that it cannot be undone when nobody is booked', () => {
    // Reached through the has_bookings way out, on a class whose bookings were all released.
    create();
    host.deleteBlocked.set(true);
    fixture.detectChanges();

    press('Odwołaj zamiast tego');

    expect(text()).toContain('Odwołać „Joga”');
    expect(text()).not.toContain('Powiadomimy');
    expect(text()).toContain('Tej operacji nie można cofnąć.');
  });

  it('turns a has_bookings refusal into the cancel step', () => {
    create();

    press('Usuń');
    expect(button('Odwołaj zamiast tego')).toBeUndefined();

    host.deleteBlocked.set(true);
    fixture.detectChanges();

    expect(text()).toContain('Zamiast usuwać, możesz odwołać');

    press('Odwołaj zamiast tego');

    expect(button('Tak, odwołaj')).toBeDefined();
    // The way out is gone once taken — it would otherwise sit under the step it led to.
    expect(button('Odwołaj zamiast tego')).toBeUndefined();
  });

  it('hands Zapisani to the screen rather than opening anything itself', () => {
    create();

    press('Zapisani');

    expect(host.bookings).toBe(1);
  });

  // --- busy, failed, closing --------------------------------------------------

  it('disables every action while a request is in flight', () => {
    create();
    host.busy.set(true);
    fixture.detectChanges();

    expect(button('Powiel')!.disabled).toBe(true);
    expect(button('Zapisani')!.disabled).toBe(true);
    expect(button('Usuń')!.disabled).toBe(true);

    host.busy.set(false);
    fixture.detectChanges();
    press('Usuń');
    host.busy.set(true);
    fixture.detectChanges();

    expect(button('Usuwanie…')!.disabled).toBe(true);
  });

  it('shows the failure it is given, without announcing it a second time', () => {
    create();
    host.failure.set('Zajęcia już się rozpoczęły.');
    fixture.detectChanges();

    const marker = element().querySelector('.field-error');

    expect(marker?.textContent).toContain('Zajęcia już się rozpoczęły.');
    // The toast already said it; a second alert would read the same failure out twice.
    expect(marker?.getAttribute('role')).toBeNull();
  });

  it('closes on the backdrop, on Zamknij and on Escape', () => {
    create();

    element().querySelector<HTMLButtonElement>('.overlay-backdrop')!.click();
    press('Zamknij');
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(host.closes).toBe(3);
  });
});
