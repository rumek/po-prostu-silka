import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ScheduledClass } from '../../../core/scheduling/class.models';
import { ClassDetailsOverlay } from './class-details-overlay';

function classAt(offsetMinutes: number, over: Partial<ScheduledClass> = {}): ScheduledClass {
  return {
    id: over.id ?? 'c1',
    classTypeId: 't1',
    name: over.name ?? 'Joga',
    description: over.description ?? null,
    startsAt: new Date(Date.now() + offsetMinutes * 60_000).toISOString(),
    durationMinutes: over.durationMinutes ?? 60,
    instructorUserId: 'u1',
    instructor: over.instructor ?? 'Ola',
    capacity: over.capacity ?? 12,
    freeSpots: over.freeSpots ?? 4,
    status: 'Scheduled',
  };
}

/** Hosts the overlay the way the schedule does, so the inputs and outputs are exercised as bound. */
@Component({
  imports: [ClassDetailsOverlay],
  template: `
    <app-class-details-overlay
      [row]="row()"
      [booked]="booked()"
      [busy]="busy()"
      [error]="error()"
      (book)="books = books + 1"
      (cancelBooking)="cancels = cancels + 1"
      (closed)="closes = closes + 1"
    />
  `,
})
class Host {
  readonly row = signal<ScheduledClass>(classAt(60));
  readonly booked = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  books = 0;
  cancels = 0;
  closes = 0;
}

/**
 * The member's booking surface. What these tests protect is that the overlay never offers an action
 * that cannot succeed AND never withholds one without saying why — a missing button with no
 * explanation reads as broken, which is the failure the admin panel's past-week note also guards.
 */
describe('ClassDetailsOverlay', () => {
  let fixture: ComponentFixture<Host>;
  let host: Host;

  function create(): void {
    TestBed.configureTestingModule({ imports: [Host] });
    fixture = TestBed.createComponent(Host);
    host = fixture.componentInstance;
    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function buttonWith(text: string): HTMLButtonElement | undefined {
    return [...element().querySelectorAll('button')].find((button) =>
      button.textContent?.includes(text),
    );
  }

  it('offers Zapisz się on a class with room that has not started', () => {
    create();

    expect(buttonWith('Zapisz się')).toBeDefined();
    expect(buttonWith('Anuluj zapis')).toBeUndefined();

    buttonWith('Zapisz się')!.click();

    expect(host.books).toBe(1);
  });

  it('offers Anuluj zapis instead once the member is booked', () => {
    create();
    host.booked.set(true);
    fixture.detectChanges();

    expect(buttonWith('Zapisz się')).toBeUndefined();

    buttonWith('Anuluj zapis')!.click();

    expect(host.cancels).toBe(1);
  });

  it('explains a full class rather than showing a button that would be refused', () => {
    create();
    host.row.set(classAt(60, { freeSpots: 0 }));
    fixture.detectChanges();

    expect(buttonWith('Zapisz się')).toBeUndefined();
    expect(element().textContent).toContain('Brak wolnych miejsc');
  });

  it('explains a class that has already started', () => {
    create();
    // A minute ago: the same boundary the server applies — booking closes AT the start.
    host.row.set(classAt(-1));
    fixture.detectChanges();

    expect(buttonWith('Zapisz się')).toBeUndefined();
    expect(element().textContent).toContain('już się rozpoczęły');
  });

  it('still offers cancel after the class has started, because cancelling is free anytime', () => {
    create();
    host.row.set(classAt(-1));
    host.booked.set(true);
    fixture.detectChanges();

    // prd.md §Non-Goals locks free-cancel-anytime. A cancel button that vanished at the start would
    // be this screen inventing a rule the server does not have.
    expect(buttonWith('Anuluj zapis')).toBeDefined();
  });

  it('shows the class type description, which nothing else in the app renders', () => {
    create();
    host.row.set(classAt(60, { description: 'Spokojna praktyka dla początkujących.' }));
    fixture.detectChanges();

    expect(element().textContent).toContain('Spokojna praktyka dla początkujących.');
  });

  it('renders a refusal inline and disables the action while one is in flight', () => {
    create();
    host.error.set('Brak wolnych miejsc na tych zajęciach.');
    host.busy.set(true);
    fixture.detectChanges();

    expect(element().querySelector('[role="alert"]')!.textContent).toContain('Brak wolnych miejsc');
    expect(buttonWith('Zapisywanie…')!.disabled).toBe(true);
  });

  it('closes on Escape, wherever focus happens to be', () => {
    create();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();

    expect(host.closes).toBe(1);
  });

  it('closes when the backdrop is activated', () => {
    create();

    element().querySelector<HTMLButtonElement>('.overlay-backdrop')!.click();

    expect(host.closes).toBe(1);
  });
});
