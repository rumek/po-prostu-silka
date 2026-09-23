import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  MyAttendanceEntry,
  MyAttendanceHistory,
  MyAttendanceSummary,
} from '../../core/scheduling/booking.models';
import { AttendanceHistory } from './attendance-history';

function entry(over: Partial<MyAttendanceEntry> = {}): MyAttendanceEntry {
  return {
    bookingId: over.bookingId ?? 'b1',
    classId: over.classId ?? 'c1',
    name: over.name ?? 'Joga',
    startsAt: over.startsAt ?? '2026-09-10T16:00:00Z',
    durationMinutes: 60,
    instructor: over.instructor ?? 'Ola',
    outcome: over.outcome ?? 'present',
  };
}

const SUMMARY: MyAttendanceSummary = {
  typeName: 'Karnet 8 wejść',
  validFrom: '2026-09-01',
  validTo: '2026-09-30',
  entryCount: 8,
  present: 3,
  absent: 1,
  unrecorded: 2,
};

function page(over: Partial<MyAttendanceHistory> = {}): MyAttendanceHistory {
  return {
    summary: over.summary === undefined ? SUMMARY : over.summary,
    items: over.items ?? [entry()],
    earlierBefore: over.earlierBefore ?? null,
  };
}

/**
 * The member's history (S-27, AT-05): month groups, a status dot per row, and "Pokaż
 * wcześniejsze" to page back. The server decides WHAT is in a page; these tests pin how it reads.
 */
describe('AttendanceHistory', () => {
  let fixture: ComponentFixture<AttendanceHistory>;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AttendanceHistory],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AttendanceHistory);
    fixture.detectChanges();
  });

  afterEach(() => controller.verify());

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function respond(body: MyAttendanceHistory): Promise<void> {
    controller.expectOne('/api/bookings/history').flush(body);
    await settle();
  }

  function headings(): string[] {
    return [...element().querySelectorAll('.class-month-title')].map(
      (h) => h.textContent?.trim() ?? '',
    );
  }

  function buttonWith(text: string): HTMLButtonElement | undefined {
    return [...element().querySelectorAll('button')].find((b) => b.textContent?.includes(text));
  }

  it('shows the loading state until the first page lands', () => {
    expect(element().querySelector('app-loading')).not.toBeNull();

    controller.expectOne('/api/bookings/history').flush(page());
  });

  it('groups by the CLUB month: 23:30 UTC on 31 August is September in Warsaw', async () => {
    await respond(
      page({
        items: [
          entry({ bookingId: 'b1', startsAt: '2026-09-10T16:00:00Z' }),
          entry({ bookingId: 'b2', startsAt: '2026-08-31T23:30:00Z' }),
          entry({ bookingId: 'b3', startsAt: '2026-08-20T16:00:00Z' }),
        ],
      }),
    );

    expect(headings()).toEqual(['Wrzesień 2026', 'Sierpień 2026']);

    const september = element().querySelectorAll('.class-month')[0];
    expect(september.querySelectorAll('li.row').length).toBe(2);
  });

  it('dots only a class attended or missed, with its word for screen readers and the tooltip', async () => {
    await respond(
      page({
        items: [
          entry({ bookingId: 'b1', outcome: 'present' }),
          entry({ bookingId: 'b2', outcome: 'absent' }),
          entry({ bookingId: 'b3', outcome: 'unrecorded' }),
          entry({ bookingId: 'b4', outcome: 'cancelled' }),
        ],
      }),
    );

    const rows = [...element().querySelectorAll<HTMLElement>('li.row')];
    const dots = rows.map((row) => row.querySelector<HTMLElement>('.outcome'));

    expect(dots.map((d) => d?.title ?? null)).toEqual(['Obecny', 'Nieobecny', null, null]);
    expect(dots[0]!.querySelector('app-icon')).not.toBeNull();
    expect(dots[0]!.querySelector('.outcome-word')!.textContent?.trim()).toBe('Obecny');

    // Unrecorded says nothing; cancelled is crossed out and keeps its word for a screen reader.
    expect(rows[2].querySelector('.outcome-word')).toBeNull();
    expect(rows[3].querySelector('.outcome-word')!.textContent?.trim()).toBe('Odwołane');
    expect(rows[3].querySelector('.booked-class-struck')!.textContent).toContain('Joga');
  });

  it('keeps the same date column and time line as the upcoming tab', async () => {
    await respond(
      page({ items: [entry({ startsAt: '2026-09-10T16:00:00Z', instructor: 'Ola' })] }),
    );

    expect(element().querySelector('app-booked-class .row-meta')!.textContent).toContain(
      '18:00 · Ola',
    );
  });

  // Weekday over day over month, in the club's zone: 16:00 UTC on 10 September is a Thursday.
  it('stacks the date as weekday, day and month, and says it in full once', async () => {
    await respond(page({ items: [entry({ startsAt: '2026-09-10T16:00:00Z' })] }));

    const date = element().querySelector('app-class-date')!;
    const small = [...date.querySelectorAll('.class-date-small')].map((e) => e.textContent?.trim());

    expect(small).toEqual(['czw.', 'wrz']);
    expect(date.querySelector('.class-date-day')!.textContent?.trim()).toBe('10');
    expect(date.querySelector('.class-date-spoken')!.textContent).toContain('10 września');
  });

  it('shows no karnet summary card', async () => {
    await respond(page());

    expect(element().querySelector('.history-summary')).toBeNull();
    expect(element().textContent).not.toContain('Karnet 8 wejść');
  });

  it('tallies each month over present and absent only', async () => {
    await respond(
      page({
        items: [
          entry({ bookingId: 'b1', outcome: 'present' }),
          entry({ bookingId: 'b2', outcome: 'present' }),
          entry({ bookingId: 'b3', outcome: 'absent' }),
          entry({ bookingId: 'b4', outcome: 'unrecorded' }),
          entry({ bookingId: 'b5', outcome: 'cancelled' }),
        ],
      }),
    );

    expect(element().querySelector('.history-month-tally')!.textContent).toContain('2/3 obecności');
  });

  it('says so when the member has no history at all', async () => {
    await respond(page({ summary: null, items: [] }));

    expect(element().querySelector('app-empty')!.textContent).toContain(
      'Nie masz jeszcze zajęć w historii',
    );
    expect(buttonWith('Pokaż wcześniejsze')).toBeUndefined();
  });

  it('appends the next three months and hides the button at the start', async () => {
    await respond(
      page({
        items: [entry({ bookingId: 'b1', startsAt: '2026-09-10T16:00:00Z' })],
        earlierBefore: '2026-07-01',
      }),
    );

    buttonWith('Pokaż wcześniejsze')!.click();
    await settle();

    controller
      .expectOne(
        (r) => r.url === '/api/bookings/history' && r.params.get('before') === '2026-07-01',
      )
      .flush(
        page({
          summary: null,
          items: [entry({ bookingId: 'b2', startsAt: '2026-05-12T16:00:00Z' })],
          earlierBefore: null,
        }),
      );
    await settle();

    expect(headings()).toEqual(['Wrzesień 2026', 'Maj 2026']);
    expect(buttonWith('Pokaż wcześniejsze')).toBeUndefined();
  });

  it('keeps the loaded months and says so under the button when paging back fails', async () => {
    await respond(page({ earlierBefore: '2026-07-01' }));

    buttonWith('Pokaż wcześniejsze')!.click();
    await settle();

    controller
      .expectOne((r) => r.url === '/api/bookings/history')
      .flush(null, { status: 500, statusText: 'Server Error' });
    await settle();

    expect(element().querySelectorAll('li.row').length).toBe(1);
    expect(element().querySelector('.history-more [role="alert"]')).not.toBeNull();
  });

  it('offers a retry when the first page fails', async () => {
    controller.expectOne('/api/bookings/history').error(new ProgressEvent('failed'));
    await settle();

    expect(element().querySelector('[role="alert"]')!.textContent).toContain(
      'Nie udało się wczytać historii',
    );

    buttonWith('Spróbuj ponownie')!.click();
    await settle();

    await respond(page());
    expect(element().querySelectorAll('li.row').length).toBe(1);
  });
});
