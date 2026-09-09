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
    instructorMemberId: 'u1',
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
    <app-class-details-overlay [row]="row()" [booked]="booked()" (closed)="closes = closes + 1" />
  `,
})
class Host {
  readonly row = signal<ScheduledClass>(classAt(60));
  readonly booked = signal(false);
  closes = 0;
}

/**
 * The member's read of one class (S-16, MP-01).
 *
 * <p>
 * WHAT THESE TESTS NOW PROTECT IS AN ABSENCE. The overlay used to offer booking and cancelling, and
 * the tests guarded that it never offered an action that could not succeed and never withheld one
 * without saying why. MP-01 removed both actions, so the equivalent guarantee is that no action
 * creeps back in — and that the member is told who does book them in, rather than being left in
 * front of a panel that simply has no button.
 * </p>
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

  /**
   * THE MP-01 REGRESSION TEST. The only button in this panel is Zamknij; a booking control appearing
   * here again would mean self-service booking had come back through the UI while the API answers
   * 405 to it.
   */
  it('offers no booking action at all, only a way out', () => {
    create();

    const labels = [...element().querySelectorAll('button')]
      .map((button) => button.textContent?.trim() ?? '')
      // The backdrop is a button so it can be reached from the keyboard; it is not an action.
      .filter((label) => label.length > 0);

    expect(labels).toEqual(['Zamknij']);
  });

  it('tells a member who is not booked that the club does the booking', () => {
    create();

    expect(element().textContent).toContain('Zapisów dokonuje klub');
  });

  it('tells a booked member that they are in, without offering to take them out', () => {
    create();
    host.booked.set(true);
    fixture.detectChanges();

    expect(element().textContent).toContain('Jesteś zapisany na te zajęcia');
    expect(element().textContent).not.toContain('Zapisów dokonuje klub');
  });

  it('still reports a full class, because free spots are information the member wants', () => {
    create();
    host.row.set(classAt(60, { freeSpots: 0 }));
    fixture.detectChanges();

    expect(element().textContent).toContain('Brak miejsc');
  });

  it('shows the class type description, which nothing else in the app renders', () => {
    create();
    host.row.set(classAt(60, { description: 'Spokojna praktyka dla początkujących.' }));
    fixture.detectChanges();

    expect(element().textContent).toContain('Spokojna praktyka dla początkujących.');
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
