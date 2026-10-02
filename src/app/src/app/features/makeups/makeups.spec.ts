import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { MakeupItem, MakeupPage } from '../../core/scheduling/makeup.models';
import { Makeups } from './makeups';

/** A deadline far ahead, so "Otwórz ponownie" is offered whatever day the suite runs. */
const FAR = '2099-12-31';

function item(over: Partial<MakeupItem> = {}): MakeupItem {
  return {
    absenceBookingId: over.absenceBookingId ?? 'a1',
    memberId: over.memberId ?? 'm1',
    displayName: over.displayName ?? 'Anna Kowalska',
    className: over.className ?? 'Joga',
    absenceStartsAt: over.absenceStartsAt ?? '2026-09-28T08:00:00Z',
    instructor: over.instructor ?? 'Ola',
    deadline: over.deadline ?? FAR,
    status: over.status ?? 'open',
    closedByHand: over.closedByHand ?? false,
    makeup: over.makeup ?? null,
  };
}

function page(items: MakeupItem[], total = items.length): MakeupPage {
  return { items, total, page: 1, pageSize: 25 };
}

const OPEN_LIST = '/api/makeups?page=1';
const CLOSED_LIST = '/api/makeups?page=1&closed=true';

/** "Odrabianie" (S-36): the staff makeup list. */
describe('Makeups', () => {
  let fixture: ComponentFixture<Makeups>;
  let controller: HttpTestingController;
  let router: Router;

  async function arrive(url = '/'): Promise<void> {
    TestBed.configureTestingModule({
      imports: [Makeups],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    controller = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);

    await router.navigateByUrl(url);
    fixture = TestBed.createComponent(Makeups);
  }

  async function createWith(rows: MakeupItem[], url = '/', request = OPEN_LIST): Promise<void> {
    await arrive(url);
    (await vi.waitFor(() => controller.expectOne(request))).flush(page(rows));
    await settle();
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => controller.verify());

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function rowOf(name: string): HTMLElement {
    return [...root().querySelectorAll<HTMLElement>('li')].find((row) =>
      row.textContent?.includes(name),
    )!;
  }

  function buttonWith(text: string, scope: ParentNode = root()): HTMLButtonElement | undefined {
    return [...scope.querySelectorAll('button')].find((button) =>
      button.textContent?.includes(text),
    );
  }

  it('lists the open items with their status and deadline', async () => {
    await createWith([item()]);

    const row = rowOf('Anna Kowalska');
    expect(row.textContent).toContain('Do odrobienia');
    expect(row.textContent).toContain('Joga');
    expect(row.textContent).toContain('Termin: do 31 grudnia');
    expect(buttonWith('Zapisz na odrabianie', row)).toBeDefined();
    expect(buttonWith('Zamknij', row)).toBeDefined();
  });

  it('shows a planned makeup and offers its release before the start', async () => {
    await createWith([
      item({
        status: 'planned',
        makeup: {
          classId: 'c2',
          bookingId: 'b2',
          name: 'Pilates',
          startsAt: '2099-01-05T17:00:00Z',
          instructor: 'Ewa',
        },
      }),
    ]);

    const row = rowOf('Anna Kowalska');
    expect(row.textContent).toContain('Zaplanowane');
    expect(row.textContent).toContain('Pilates');
    expect(buttonWith('Zwolnij', row)).toBeDefined();
    expect(buttonWith('Zapisz na odrabianie', row)).toBeUndefined();
  });

  it('reads the closed view from the URL and offers a reopen within the deadline', async () => {
    await createWith(
      [item({ status: 'not_made_up', closedByHand: true })],
      '/?zamkniete=1',
      CLOSED_LIST,
    );

    const row = rowOf('Anna Kowalska');
    expect(row.textContent).toContain('Nie odrobione');
    expect(row.textContent).toContain('Zamknięte ręcznie');
    expect(buttonWith('Otwórz ponownie', row)).toBeDefined();
    expect((root().querySelector('#makeups-closed') as HTMLInputElement).checked).toBe(true);
  });

  it('toggles the closed view through the URL, replacing the entry', async () => {
    await createWith([item()]);
    const navigate = vi.spyOn(router, 'navigate');

    (root().querySelector('#makeups-closed') as HTMLInputElement).click();
    await settle();

    expect(navigate).toHaveBeenCalledWith(
      [],
      expect.objectContaining({ queryParams: { zamkniete: '1', page: null }, replaceUrl: true }),
    );
    (await vi.waitFor(() => controller.expectOne(CLOSED_LIST))).flush(page([]));
    await settle();
  });

  it('closes an item and replaces the row with the server answer', async () => {
    await createWith([item()]);

    buttonWith('Zamknij', rowOf('Anna Kowalska'))!.click();
    await settle();

    const request = controller.expectOne('/api/makeups/a1/closed');
    expect(request.request.method).toBe('PUT');
    request.flush(item({ status: 'not_made_up', closedByHand: true }));
    await settle();

    expect(rowOf('Anna Kowalska').textContent).toContain('Nie odrobione');
  });

  it('opens the class picker for an open item', async () => {
    await createWith([item()]);

    buttonWith('Zapisz na odrabianie', rowOf('Anna Kowalska'))!.click();
    await settle();

    expect(root().querySelector('app-makeup-class-picker')).not.toBeNull();
    controller.expectOne('/api/makeups/a1/classes').flush([]);
    await settle();
  });

  it('says nobody is waiting when the open list is empty', async () => {
    await createWith([]);

    expect(root().textContent).toContain('Nikt nie czeka na odrobienie zajęć.');
  });

  it('keeps the screen state on a failed load, with a retry', async () => {
    await arrive();
    (await vi.waitFor(() => controller.expectOne(OPEN_LIST))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();

    expect(root().querySelector('.alert')).not.toBeNull();
    expect(buttonWith('Spróbuj ponownie')).toBeDefined();
  });
});
