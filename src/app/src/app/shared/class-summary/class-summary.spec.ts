import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ClassSummary } from './class-summary';

/**
 * The shared class block used by `/my-classes` and, from S-12, the dashboard cards.
 *
 * What these tests protect is the end time, because it is the only thing this component DERIVES
 * rather than displays — neither API returns one. The formatting itself is Angular's DatePipe and is
 * not re-tested here.
 */
describe('ClassSummary', () => {
  let fixture: ComponentFixture<ClassSummary>;

  function create(over: Partial<Record<string, unknown>> = {}) {
    TestBed.configureTestingModule({ imports: [ClassSummary] });

    fixture = TestBed.createComponent(ClassSummary);
    fixture.componentRef.setInput('name', over['name'] ?? 'Joga');
    // A fixed LOCAL wall-clock time: the component renders in local time, so a UTC literal would make
    // the expected output depend on the machine's zone.
    fixture.componentRef.setInput(
      'startsAt',
      over['startsAt'] ?? new Date('2026-09-07T18:00').toISOString(),
    );
    fixture.componentRef.setInput('durationMinutes', over['durationMinutes'] ?? 60);
    fixture.componentRef.setInput('instructor', over['instructor'] ?? 'Ola');
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('renders the class name and the instructor', () => {
    create({ name: 'Pilates', instructor: 'Ala' });

    expect(text()).toContain('Pilates');
    expect(text()).toContain('Ala');
  });

  it('derives the end time from the duration', () => {
    create({ durationMinutes: 90 });

    expect(text()).toContain('18:00');
    expect(text()).toContain('19:30');
  });

  /** A class crossing midnight still ends on the clock the member reads, not the day. */
  it('handles a duration that crosses midnight', () => {
    create({ startsAt: new Date('2026-09-07T23:30').toISOString(), durationMinutes: 60 });

    expect(text()).toContain('23:30');
    expect(text()).toContain('00:30');
  });
});
