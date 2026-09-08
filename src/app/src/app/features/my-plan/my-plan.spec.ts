import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TrainingPlanDetail } from '../../core/training/training-plan.models';
import { MyPlan } from './my-plan';

const URL = '/api/plans/mine';

const PLAN: TrainingPlanDetail = {
  id: 'p1',
  name: 'Masa - jesień',
  memberId: 'm1',
  memberDisplayName: 'Anna Kowalska',
  assignedByDisplayName: 'Marek Trener',
  createdAt: new Date('2026-09-01T10:00').toISOString(),
  items: [
    {
      id: 'i1',
      exerciseId: 'e1',
      exerciseName: 'Przysiad ze sztangą',
      position: 0,
      sets: 4,
      reps: '8-12',
      weightKg: 60,
      restSeconds: 120,
      note: 'Kolana na zewnątrz.',
      durationSeconds: null,
      muscleGroup: 'Nogi',
    },
    {
      id: 'i2',
      exerciseId: 'e2',
      exerciseName: 'Wyciskanie leżąc',
      position: 1,
      sets: null,
      reps: null,
      weightKg: null,
      restSeconds: null,
      note: null,
      durationSeconds: null,
      muscleGroup: null,
    },
  ],
};

describe('MyPlan', () => {
  let fixture: ComponentFixture<MyPlan>;
  let controller: HttpTestingController;

  async function createWith(
    body: TrainingPlanDetail | null,
    options?: { status: number; statusText: string },
  ) {
    TestBed.configureTestingModule({
      imports: [MyPlan],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyPlan);

    (await vi.waitFor(() => controller.expectOne(URL))).flush(body, options);
    await settle();
  }

  afterEach(() => controller.verify());

  /**
   * TWO ROUNDS, unlike the other specs here. `getMine()` is the one service method that awaits and
   * then post-processes (a 204 body becomes null), so its promise settles one microtask after the
   * response - a single whenStable/detectChanges pair renders the loading state and stops.
   */
  async function settle() {
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function html(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function itemNames(): string[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.my-plan-name')).map(
      (el) => el.textContent?.trim() ?? '',
    );
  }

  it('renders the plan, its author and its prescription', async () => {
    await createWith(PLAN);

    expect(html()).toContain('Masa - jesień');
    expect(html()).toContain('Marek Trener');
    expect(html()).toContain('8-12');
    expect(html()).toContain('60');
    expect(html()).toContain('Kolana na zewnątrz.');
  });

  /** The order is the prescription. The API sorts by position; nothing on this screen re-sorts. */
  it('keeps the trainer order', async () => {
    await createWith(PLAN);

    expect(itemNames()).toEqual(['Przysiad ze sztangą', 'Wyciskanie leżąc']);
  });

  /**
   * A trainer may prescribe a bare exercise (FR-015 makes every parameter optional). Absent fields
   * are OMITTED rather than shown as dashes, so the second item renders its name and nothing else.
   */
  it('omits the parameters an item does not carry', async () => {
    await createWith(PLAN);

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('.my-plan-item');

    expect(rows[0].querySelectorAll('.my-plan-params li')).toHaveLength(4);
    expect(rows[1].querySelector('.my-plan-params')).toBeNull();
  });

  /**
   * The name is plain text and the INFO ICON carries the navigation (S-15). Icon-only, so the label
   * names the exercise: ten links all reading "Opis ćwiczenia" would be useless in a screen
   * reader's element list, which is the only place they are read apart from each other.
   */
  it('links each item to its exercise through a named info control', async () => {
    await createWith(PLAN);

    const root = fixture.nativeElement as HTMLElement;
    const info = root.querySelector<HTMLAnchorElement>('.my-plan-info');

    expect(info!.getAttribute('href')).toBe('/my-plan/exercises/e1');
    expect(info!.getAttribute('aria-label')).toBe('Opis ćwiczenia: Przysiad ze sztangą');

    // The name itself is no longer a link.
    expect(root.querySelector('.my-plan-name')!.tagName).not.toBe('A');
  });

  /**
   * The muscle group is a fact about the exercise, so it renders when the library carries one and is
   * ABSENT from the DOM when it does not — not an empty line. The fixture's second item has none.
   */
  it('shows the muscle group only when the exercise has one', async () => {
    await createWith(PLAN);

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('.my-plan-item');

    expect(rows[0].querySelector('.my-plan-muscle')!.textContent!.trim()).toBe('Nogi');
    expect(rows[1].querySelector('.my-plan-muscle')).toBeNull();
  });

  /**
   * THE REGRESSION THIS PHASE IS MOST LIKELY TO SHIP. The whole parameter list sits behind
   * hasParameters(); a plank prescribed with ONLY a duration is the case the column was added for,
   * and if the guard does not know about duration the card renders a name and nothing else — with
   * no error anywhere.
   */
  it('renders the parameter list for a prescription carrying only a duration', async () => {
    await createWith({
      ...PLAN,
      items: [
        {
          ...PLAN.items[1],
          exerciseName: 'Deska',
          durationSeconds: 45,
        },
      ],
    });

    const params = (fixture.nativeElement as HTMLElement).querySelectorAll('.my-plan-params li');

    expect(params).toHaveLength(1);
    expect(params[0].textContent).toContain('45');
    expect(html()).toContain('Czas');
  });

  /** The note is set off as a callout with its own icon, rather than reading as one more number. */
  it('renders the trainer note as a callout with an icon', async () => {
    await createWith(PLAN);

    const note = (fixture.nativeElement as HTMLElement).querySelector('.my-plan-note');

    expect(note!.querySelector('app-icon')).not.toBeNull();
    expect(note!.querySelector('span')!.textContent).toBe('Kolana na zewnątrz.');
  });

  /**
   * THE DISTINCTION THIS SCREEN EXISTS TO PRESERVE. The API answers 204 for "no plan", not 404, and
   * a member who has not been given one has nothing to retry — so the empty state is a plain card
   * with no retry button and no alert.
   */
  it('shows an empty card, not an error, when there is no plan', async () => {
    await createWith(null);

    expect(html()).toContain('Nie masz jeszcze przypisanego planu');
    expect(html()).not.toContain('Spróbuj ponownie');
    expect((fixture.nativeElement as HTMLElement).querySelector('.alert')).toBeNull();
  });

  /** ...and the other half of that distinction: a real failure IS an error, and offers a retry. */
  it('shows a retryable error when the request fails', async () => {
    await createWith(null, { status: 500, statusText: 'Server Error' });

    expect(html()).toContain('Nie udało się wczytać planu');
    expect(html()).not.toContain('Nie masz jeszcze przypisanego planu');

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.link-button')!
      .click();
    (await vi.waitFor(() => controller.expectOne(URL))).flush(PLAN);
    await settle();

    expect(html()).toContain('Masa - jesień');
  });

  /** A failed refetch must not leave the previous plan on screen under an error banner. */
  it('drops a stale plan when a refetch fails', async () => {
    await createWith(PLAN);

    // Cast, following schedule.spec.ts: load() is protected, and there is no UI path to a refetch
    // from the loaded state - the retry button exists only on the error screen.
    void (fixture.componentInstance as unknown as { load: () => Promise<void> }).load();
    (await vi.waitFor(() => controller.expectOne(URL))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();

    expect(html()).not.toContain('Masa - jesień');
    expect(html()).toContain('Nie udało się wczytać planu');
  });
});
