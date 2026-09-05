import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { ExerciseSummary } from '../../../core/training/exercise.models';
import {
  AssignableMember,
  TrainingPlanDetail,
  TrainingPlanRequest,
} from '../../../core/training/training-plan.models';
import { PlanBuilder } from './plan-builder';

const MEMBERS: AssignableMember[] = [
  { id: 'm1', displayName: 'Anna Kowalska' },
  { id: 'm2', displayName: 'Piotr Nowak' },
];

function exercise(id: string, name: string, isActive = true): ExerciseSummary {
  return {
    id,
    name,
    description: null,
    muscleGroup: null,
    difficulty: null,
    equipment: null,
    preparation: null,
    startingPosition: null,
    execution: null,
    videoId: null,
    isActive,
    createdAt: new Date('2026-09-01T10:00').toISOString(),
  };
}

const LIBRARY: ExerciseSummary[] = [
  exercise('e1', 'Przysiad ze sztangą'),
  exercise('e2', 'Wyciskanie leżąc'),
  exercise('e3', 'Martwy ciąg'),
  exercise('e4', 'Wycofane ćwiczenie', false),
];

const PLAN: TrainingPlanDetail = {
  id: 'p1',
  name: 'Masa - jesień',
  memberUserId: 'm1',
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
    },
    {
      id: 'i2',
      exerciseId: 'e2',
      exerciseName: 'Wyciskanie leżąc',
      position: 1,
      sets: 3,
      reps: '10',
      weightKg: null,
      restSeconds: null,
      note: null,
    },
  ],
};

describe('PlanBuilder', () => {
  let fixture: ComponentFixture<PlanBuilder>;
  let controller: HttpTestingController;

  /** `id` null creates; a value edits, and the plan load is flushed too. */
  async function create(id: string | null, plan?: TrainingPlanDetail) {
    TestBed.configureTestingModule({
      imports: [PlanBuilder],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map(id ? [['id', id]] : []) } },
        },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PlanBuilder);

    (await vi.waitFor(() => controller.expectOne('/api/trainer/plans/members'))).flush(MEMBERS);
    (await vi.waitFor(() => controller.expectOne('/api/admin/exercises'))).flush(LIBRARY);

    if (id) {
      (await vi.waitFor(() => controller.expectOne(`/api/trainer/plans/${id}`))).flush(
        plan ?? PLAN,
      );
    }

    await settle();
  }

  afterEach(() => controller.verify());

  async function settle() {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function html(): string {
    return root().textContent ?? '';
  }

  function pickable(): string[] {
    return Array.from(root().querySelectorAll('.builder-pick-name')).map(
      (el) => el.textContent?.trim() ?? '',
    );
  }

  function itemNames(): string[] {
    return Array.from(root().querySelectorAll('.builder-item-name')).map(
      (el) => el.textContent?.trim() ?? '',
    );
  }

  async function pick(name: string) {
    const index = pickable().indexOf(name);
    root().querySelectorAll<HTMLButtonElement>('.builder-pick')[index].click();
    await settle();
  }

  async function setValue(selector: string, value: string) {
    const input = root().querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  }

  async function submit(): Promise<void> {
    root().querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  function sentBody(request: TestRequest): TrainingPlanRequest {
    return request.request.body as TrainingPlanRequest;
  }

  /**
   * A retired exercise must not be prescribed anew — the server refuses `inactive_exercise`. The
   * library endpoint serves the admin's list, which needs the retired rows, so the filter is here.
   */
  it('offers only active exercises', async () => {
    await create(null);

    expect(pickable()).toEqual(['Przysiad ze sztangą', 'Wyciskanie leżąc', 'Martwy ciąg']);
    expect(html()).not.toContain('Wycofane ćwiczenie');
  });

  /**
   * The server refuses `duplicate_exercise`, and a picker that offers a choice the save will reject
   * explains worse than one that stops offering it.
   */
  it('stops offering an exercise once it is in the plan', async () => {
    await create(null);
    await pick('Wyciskanie leżąc');

    expect(itemNames()).toEqual(['Wyciskanie leżąc']);
    expect(pickable()).toEqual(['Przysiad ze sztangą', 'Martwy ciąg']);
  });

  it('puts a removed exercise back into the picker', async () => {
    await create(null);
    await pick('Martwy ciąg');

    root().querySelector<HTMLButtonElement>('.builder-remove')!.click();
    await settle();

    expect(itemNames()).toEqual([]);
    expect(pickable()).toContain('Martwy ciąg');
  });

  /**
   * THE ARRAY ORDER IS THE CONTRACT. No position field is sent — the server numbers what it
   * receives — so this asserts the items arrive in the order the trainer built them.
   */
  it('sends the items in order, with blanks collapsed to null', async () => {
    await create(null);

    await setValue('#plan-name', 'Masa - jesień');
    const member = root().querySelector<HTMLSelectElement>('#plan-member')!;
    member.value = 'm1';
    member.dispatchEvent(new Event('change'));
    await settle();

    await pick('Przysiad ze sztangą');
    await pick('Martwy ciąg');

    await setValue('#sets-0', '4');
    await setValue('#reps-0', '8-12');

    await submit();

    const request = await vi.waitFor(() =>
      controller.expectOne((r) => r.url === '/api/trainer/plans' && r.method === 'POST'),
    );
    const body = sentBody(request);

    expect(body.name).toBe('Masa - jesień');
    expect(body.memberUserId).toBe('m1');
    expect(body.items.map((i) => i.exerciseId)).toEqual(['e1', 'e3']);
    expect(body.items[0]).toMatchObject({ sets: 4, reps: '8-12' });
    // Untouched optional fields go as null, not as 0 or "".
    expect(body.items[0].weightKg).toBeNull();
    expect(body.items[0].note).toBeNull();
    expect(body.items[1]).toMatchObject({ sets: null, reps: null });

    request.flush(PLAN);
  });

  it('loads an existing plan in its stored order and PUTs to the same id', async () => {
    await create('p1');

    expect(itemNames()).toEqual(['Przysiad ze sztangą', 'Wyciskanie leżąc']);
    expect(root().querySelector<HTMLInputElement>('#plan-name')!.value).toBe('Masa - jesień');

    await submit();

    const request = await vi.waitFor(() =>
      controller.expectOne((r) => r.url === '/api/trainer/plans/p1' && r.method === 'PUT'),
    );

    // getRawValue, not value: the member control is disabled while editing, and an omitted
    // memberUserId would be refused with `member_changed`.
    expect(sentBody(request).memberUserId).toBe('m1');
    expect(sentBody(request).items).toHaveLength(2);

    request.flush(PLAN);
  });

  /** A plan does not move between people; it is superseded. */
  it('locks the member while editing', async () => {
    await create('p1');

    expect(root().querySelector<HTMLSelectElement>('#plan-member')!.disabled).toBe(true);
  });

  /** An empty plan is refused by the server too (`no_items`); saying so here costs no round trip. */
  it('refuses to submit an empty plan without calling the API', async () => {
    await create(null);

    await setValue('#plan-name', 'Pusty');
    await submit();

    expect(html()).toContain('Dodaj przynajmniej jedno ćwiczenie');
    controller.expectNone('/api/trainer/plans');
  });

  it('puts a name refusal on the name control', async () => {
    await create('p1');
    await submit();

    (await vi.waitFor(() => controller.expectOne('/api/trainer/plans/p1'))).flush(
      { reason: 'name_too_long' },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(html()).toContain('Nazwa może mieć najwyżej');
  });

  /** A 409 from a concurrent assignment is a "try again", not a validation error. */
  it('explains a concurrent-change conflict', async () => {
    await create('p1');
    await submit();

    (await vi.waitFor(() => controller.expectOne('/api/trainer/plans/p1'))).flush(
      { reason: 'conflict' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(html()).toContain('Odśwież stronę i spróbuj ponownie');
  });

  /**
   * Without a member there is no plan to save, so a failed picker fetch is surfaced rather than
   * swallowed the way ExerciseForm's optional datalist is.
   */
  it('says so when the member list cannot be loaded', async () => {
    TestBed.configureTestingModule({
      imports: [PlanBuilder],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map() } } },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PlanBuilder);

    (await vi.waitFor(() => controller.expectOne('/api/trainer/plans/members'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    (await vi.waitFor(() => controller.expectOne('/api/admin/exercises'))).flush(LIBRARY);
    await settle();

    expect(html()).toContain('Nie udało się wczytać listy członków');
  });
});
