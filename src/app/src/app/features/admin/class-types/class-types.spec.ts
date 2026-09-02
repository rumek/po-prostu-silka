import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ClassTypeSummary } from '../../../core/scheduling/class-type.models';
import { ClassTypes } from './class-types';

const JOGA: ClassTypeSummary = {
  id: 't1',
  name: 'Joga dla początkujących',
  description: 'Spokojne zajęcia dla osób bez doświadczenia.',
  defaultDurationMinutes: 60,
  defaultCapacity: 12,
  isActive: true,
  createdAt: new Date('2026-09-01T10:00').toISOString(),
};

const RETIRED: ClassTypeSummary = {
  ...JOGA,
  id: 't2',
  name: 'Zumba',
  description: null,
  isActive: false,
};

describe('ClassTypes', () => {
  let fixture: ComponentFixture<ClassTypes>;
  let controller: HttpTestingController;

  async function createWith(rows: ClassTypeSummary[]) {
    TestBed.configureTestingModule({
      imports: [ClassTypes],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ClassTypes);

    (await vi.waitFor(() => controller.expectOne('/api/admin/class-types'))).flush(rows);
    await settle();
  }

  afterEach(() => controller.verify());

  async function settle() {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function html(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function rows(): HTMLElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.class-types-row'));
  }

  function buttonIn(row: HTMLElement, label: string): HTMLButtonElement {
    return Array.from(row.querySelectorAll('button')).find((b) =>
      (b.textContent ?? '').includes(label),
    )!;
  }

  function toggle(): HTMLInputElement {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      '.class-types-toggle input',
    )!;
  }

  it('renders one row per type, with its defaults and description', async () => {
    await createWith([JOGA]);

    expect(rows().length).toBe(1);
    expect(html()).toContain('Joga dla początkujących');
    expect(html()).toContain('Spokojne zajęcia');
    expect(html()).toContain('60');
    expect(html()).toContain('12');
  });

  /** The toggle is off by default: retired types are the exception and must not crowd the list. */
  it('hides inactive types until the toggle is set', async () => {
    await createWith([JOGA, RETIRED]);

    expect(rows().length).toBe(1);
    expect(html()).not.toContain('Zumba');

    toggle().click();
    await settle();

    expect(rows().length).toBe(2);
    expect(html()).toContain('Zumba');
    expect(html()).toContain('Nieaktywny');
  });

  it('offers Dezaktywuj on an active row and Aktywuj on an inactive one', async () => {
    await createWith([JOGA, RETIRED]);
    toggle().click();
    await settle();

    expect(buttonIn(rows()[0], 'Dezaktywuj')).toBeTruthy();
    expect(buttonIn(rows()[1], 'Aktywuj')).toBeTruthy();
  });

  /**
   * Patched in place from the response rather than refetched: a second round trip would buy nothing
   * and would reorder the list under the admin's cursor.
   */
  it('updates the row in place after a deactivation, without refetching', async () => {
    await createWith([JOGA]);

    buttonIn(rows()[0], 'Dezaktywuj').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/class-types/t1/deactivate'))).flush({
      ...JOGA,
      isActive: false,
    });
    await settle();

    // Now hidden by the default filter — proof the row really changed rather than being re-rendered
    // from stale state. afterEach's verify() proves no list refetch was issued.
    expect(rows().length).toBe(0);

    toggle().click();
    await settle();

    expect(rows().length).toBe(1);
    expect(html()).toContain('Nieaktywny');
  });

  it('reactivates an inactive type', async () => {
    await createWith([RETIRED]);
    toggle().click();
    await settle();

    buttonIn(rows()[0], 'Aktywuj').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/class-types/t2/activate'))).flush({
      ...RETIRED,
      isActive: true,
    });
    await settle();

    expect(html()).not.toContain('Nieaktywny');
  });

  /**
   * The sharpest edge in the slice. The activate request carries no name, so nothing on screen
   * suggests a name can clash — but deactivating released the name and another type may hold it now.
   * The message has to explain that, because there is no control to attach it to.
   */
  it('explains a name clash when a reactivation is refused', async () => {
    await createWith([RETIRED]);
    toggle().click();
    await settle();

    buttonIn(rows()[0], 'Aktywuj').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/class-types/t2/activate'))).flush(
      { reason: 'name_taken' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(html()).toContain('jest teraz zajęta');
    expect(html()).toContain('Zumba');
    // Still inactive: a refused activation must not look like it worked.
    expect(html()).toContain('Nieaktywny');
  });

  it('keeps the row and surfaces the error when an action fails', async () => {
    await createWith([JOGA]);

    buttonIn(rows()[0], 'Dezaktywuj').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/class-types/t1/deactivate'))).flush(
      null,
      { status: 500, statusText: 'Server Error' },
    );
    await settle();

    expect(rows().length).toBe(1);
    expect(html()).toContain('Nie udało się');
  });

  /** One slow row must not disable the list — the busy flag is per row, not global. */
  it('marks only the acting row busy', async () => {
    await createWith([JOGA, { ...JOGA, id: 't3', name: 'Pilates' }]);

    buttonIn(rows()[0], 'Dezaktywuj').click();
    await settle();

    expect(buttonIn(rows()[0], 'Dezaktywowanie…').disabled).toBe(true);
    expect(buttonIn(rows()[1], 'Dezaktywuj').disabled).toBe(false);

    (await vi.waitFor(() => controller.expectOne('/api/admin/class-types/t1/deactivate'))).flush({
      ...JOGA,
      isActive: false,
    });
    await settle();
  });

  it('renders an explicit empty state rather than a blank page', async () => {
    await createWith([]);

    expect(rows().length).toBe(0);
    expect(html()).toContain('Nie zdefiniowano jeszcze');
  });

  /** "None exist" and "all are hidden" are different problems and need different messages. */
  it('distinguishes an empty list from one the filter has emptied', async () => {
    await createWith([RETIRED]);

    expect(rows().length).toBe(0);
    expect(html()).toContain('Wszystkie typy są nieaktywne');
    expect(html()).not.toContain('Nie zdefiniowano jeszcze');
  });

  it('reports a failed load and offers a retry', async () => {
    TestBed.configureTestingModule({
      imports: [ClassTypes],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ClassTypes);

    (await vi.waitFor(() => controller.expectOne('/api/admin/class-types'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();

    expect(html()).toContain('Nie udało się wczytać');

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.link-button')!
      .click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/class-types'))).flush([JOGA]);
    await settle();

    expect(rows().length).toBe(1);
  });
});
