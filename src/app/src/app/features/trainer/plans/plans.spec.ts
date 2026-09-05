import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TrainingPlanSummary } from '../../../core/training/training-plan.models';
import { Plans } from './plans';

const URL = '/api/trainer/plans';

const ROWS: TrainingPlanSummary[] = [
  {
    id: 'p1',
    name: 'Masa - jesień',
    memberUserId: 'm1',
    memberDisplayName: 'Anna Kowalska',
    assignedByDisplayName: 'Marek Trener',
    createdAt: new Date('2026-09-01T10:00').toISOString(),
    itemCount: 1,
  },
  {
    id: 'p2',
    name: 'Redukcja',
    memberUserId: 'm2',
    memberDisplayName: 'Piotr Nowak',
    assignedByDisplayName: 'Marek Trener',
    createdAt: new Date('2026-09-02T10:00').toISOString(),
    itemCount: 5,
  },
];

describe('Plans', () => {
  let fixture: ComponentFixture<Plans>;
  let controller: HttpTestingController;

  async function createWith(
    body: TrainingPlanSummary[] | null,
    options?: { status: number; statusText: string },
  ) {
    TestBed.configureTestingModule({
      imports: [Plans],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Plans);

    (await vi.waitFor(() => controller.expectOne(URL))).flush(body, options);
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

  function members(): string[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.plans-member')).map(
      (el) => el.textContent?.trim() ?? '',
    );
  }

  function type(value: string): void {
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      'input[type="search"]',
    )!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('lists every active plan by member', async () => {
    await createWith(ROWS);

    expect(members()).toEqual(['Anna Kowalska', 'Piotr Nowak']);
    expect(html()).toContain('Masa - jesień');
    expect(html()).toContain('Marek Trener');
  });

  /** Polish has three plural forms, and 1 / 5 fall on two different ones. */
  it('counts exercises with the right Polish plural', async () => {
    await createWith(ROWS);

    expect(html()).toContain('1 ćwiczenie');
    expect(html()).toContain('5 ćwiczeń');
  });

  it('filters on member name and on plan name', async () => {
    await createWith(ROWS);

    type('anna');
    expect(members()).toEqual(['Anna Kowalska']);

    type('redukcja');
    expect(members()).toEqual(['Piotr Nowak']);
  });

  /** "Nothing matches your search" is a different message from "no plans exist". */
  it('distinguishes a filtered-out list from an empty one', async () => {
    await createWith(ROWS);

    type('zzz');
    expect(html()).toContain('Żaden plan nie pasuje');
    expect(html()).not.toContain('Nie przypisano jeszcze');
  });

  it('shows an empty state when no plan has been assigned', async () => {
    await createWith([]);

    expect(html()).toContain('Nie przypisano jeszcze żadnego planu');
  });

  it('offers a retry when the list fails to load', async () => {
    await createWith(null, { status: 500, statusText: 'Server Error' });

    expect(html()).toContain('Nie udało się wczytać planów');

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.link-button')!
      .click();
    (await vi.waitFor(() => controller.expectOne(URL))).flush(ROWS);
    await settle();

    expect(members()).toEqual(['Anna Kowalska', 'Piotr Nowak']);
  });

  it('links each row to its builder and offers a way to a new plan', async () => {
    await createWith(ROWS);

    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector<HTMLAnchorElement>('.plans-actions a')!.getAttribute('href')).toBe(
      '/trainer/plans/p1',
    );
    expect(root.querySelector<HTMLAnchorElement>('.plans-header a')!.getAttribute('href')).toBe(
      '/trainer/plans/new',
    );
  });
});
