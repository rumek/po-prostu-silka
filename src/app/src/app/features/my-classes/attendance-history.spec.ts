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
 * The member's history (S-27, AT-05): a summary, month groups, a chip per row, and "Pokaż
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
    return [...element().querySelectorAll('.history-month-title')].map(
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

    const september = element().querySelectorAll('.history-month')[0];
    expect(september.querySelectorAll('li.row').length).toBe(2);
  });

  it('carries a word and an icon per outcome, never colour alone', async () => {
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

    const chips = [...element().querySelectorAll('.history-row .chip')];

    expect(chips.map((c) => c.textContent?.trim())).toEqual([
      'Obecny',
      'Nieobecny',
      'Nie odnotowano',
      'Odwołane',
    ]);
    expect(chips.every((c) => c.querySelector('app-icon') !== null)).toBe(true);
    expect(element().querySelector('.history-cancelled')!.textContent).toContain('Joga');
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

  it('shows the karnet summary with its three counts and one dot per entry', async () => {
    await respond(page());

    const summary = element().querySelector('.history-summary')!;
    expect(summary.textContent).toContain('Karnet 8 wejść');
    expect(summary.textContent).toContain('do 30 września');
    expect(summary.querySelectorAll('.history-count').length).toBe(3);
    expect(summary.querySelectorAll('.history-dot').length).toBe(8);
    expect(summary.querySelectorAll('.history-dot--free').length).toBe(2);
    expect(summary.querySelector('.history-dots')!.getAttribute('aria-hidden')).toBe('true');
  });

  it('has no summary card without a covering karnet', async () => {
    await respond(page({ summary: null }));

    expect(element().querySelector('.history-summary')).toBeNull();
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
    // The summary from the first page stays.
    expect(element().querySelector('.history-summary')).not.toBeNull();
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
