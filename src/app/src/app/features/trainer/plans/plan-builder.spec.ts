import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, ParamMap, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { ExerciseSummary } from '../../../core/training/exercise.models';
import {
  AssignableMember,
  TrainingPlanDetail,
  TrainingPlanRequest,
} from '../../../core/training/training-plan.models';
import { ToastService } from '../../../shared/toast/toast.service';
import { PlanBuilder } from './plan-builder';

const MEMBER: AssignableMember = { id: 'm1', displayName: 'Anna Kowalska', hasAccount: true };

// A person the club recorded who never registered (S-14) — a plan for them is labelled as such.
const ACCOUNTLESS: AssignableMember = { id: 'm2', displayName: 'Piotr Nowak', hasAccount: false };

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
      sets: 3,
      reps: '10',
      weightKg: null,
      restSeconds: null,
      note: null,
      // Prescribed in time rather than in load — the shape S-15's column exists for.
      durationSeconds: 45,
      muscleGroup: 'Brzuch',
    },
  ],
};

describe('PlanBuilder', () => {
  let fixture: ComponentFixture<PlanBuilder>;
  let controller: HttpTestingController;
  let params: BehaviorSubject<ParamMap>;

  /**
   * Mounts the builder for a MEMBER (S-22): the route carries the member id and the list to return
   * to, and the load answers with the member and their plan — null when they have none.
   */
  function configure(memberId: string, membersLink = '/admin/members') {
    params = new BehaviorSubject(convertToParamMap({ id: memberId }));

    TestBed.configureTestingModule({
      imports: [PlanBuilder],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: params,
            snapshot: { data: { parent: membersLink } },
          },
        },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PlanBuilder);
  }

  async function create(
    plan: TrainingPlanDetail | null,
    options: { member?: AssignableMember; membersLink?: string } = {},
  ) {
    const member = options.member ?? MEMBER;
    configure(member.id, options.membersLink);

    (await vi.waitFor(() => controller.expectOne('/api/admin/exercises'))).flush(LIBRARY);
    (await vi.waitFor(() => controller.expectOne(`/api/trainer/members/${member.id}/plan`))).flush({
      member,
      plan,
    });

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

  function lastToast() {
    return TestBed.inject(ToastService).toasts().at(-1);
  }

  function submitLabel(): string {
    return root().querySelector('button[type="submit"]')!.textContent?.trim() ?? '';
  }

  /**
   * S-22: the member is fixed by the URL, so the heading names them and there is no picker at all.
   * A plan for someone with no login is real work they will not see in the app until they claim
   * their record — so the screen says which is which.
   */
  it('names the member in the heading and offers no member picker', async () => {
    await create(null, { member: ACCOUNTLESS });

    expect(root().querySelector('h1')!.textContent).toContain('Plan — Piotr Nowak');
    expect(html()).toContain('Bez konta');
    expect(root().querySelector('select')).toBeNull();
  });

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
  it("POSTs a plan-less member's first plan with the route's member id, items in order", async () => {
    await create(null);

    expect(submitLabel()).toBe('Przypisz plan');

    await setValue('#plan-name', 'Masa - jesień');

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
    expect(body.memberId).toBe('m1');
    expect(body.items.map((i) => i.exerciseId)).toEqual(['e1', 'e3']);
    expect(body.items[0]).toMatchObject({ sets: 4, reps: '8-12' });
    // Untouched optional fields go as null, not as 0 or "".
    expect(body.items[0].weightKg).toBeNull();
    expect(body.items[0].note).toBeNull();
    expect(body.items[0].durationSeconds).toBeNull();
    expect(body.items[1]).toMatchObject({ sets: null, reps: null });

    request.flush(PLAN);
  });

  /**
   * S-15. Two halves of one contract: a stored duration reaches the control, and an EDITED one
   * reaches the request. The second half is what a missing `toRequest` mapping would break — the
   * form would look right and silently drop the value on save.
   */
  it('loads a prescribed duration and sends it back on save', async () => {
    await create(PLAN);

    const stored = root().querySelector<HTMLInputElement>('#duration-1')!;
    expect(stored.value).toBe('45');

    await setValue('#duration-0', '30');
    await submit();

    const request = await vi.waitFor(() =>
      controller.expectOne((r) => r.url === '/api/trainer/plans/p1' && r.method === 'PUT'),
    );
    const body = sentBody(request);

    expect(body.items[0].durationSeconds).toBe(30);
    expect(body.items[1].durationSeconds).toBe(45);

    request.flush(PLAN);
  });

  /**
   * The floor is 1, not 0 — the one place duration does not mirror rest. Asserted on the CONTROL
   * because the server's matching refusal is already pinned in TrainingPlanEndpointTests; what this
   * guards is the mirror drifting out of step with it.
   */
  it('refuses a zero duration in the form', async () => {
    await create(PLAN);

    await setValue('#duration-0', '0');

    const control = root().querySelector<HTMLInputElement>('#duration-0')!;
    expect(control.value).toBe('0');
    expect(root().querySelector('form')!.checkValidity()).toBe(false);
  });

  /**
   * The member id in the body comes from the URL. On edit the server compares it with the stored
   * plan (`member_changed`), which makes it the URL/body agreement check.
   */
  it("loads a member's plan in its stored order and PUTs it with the route's member id", async () => {
    await create(PLAN);

    expect(itemNames()).toEqual(['Przysiad ze sztangą', 'Wyciskanie leżąc']);
    expect(root().querySelector<HTMLInputElement>('#plan-name')!.value).toBe('Masa - jesień');
    expect(submitLabel()).toBe('Zapisz zmiany');

    await submit();

    const request = await vi.waitFor(() =>
      controller.expectOne((r) => r.url === '/api/trainer/plans/p1' && r.method === 'PUT'),
    );

    expect(sentBody(request).memberId).toBe('m1');
    expect(sentBody(request).items).toHaveLength(2);

    request.flush(PLAN);
  });

  /**
   * THE SCREEN BECOMES THE EDIT VIEW after a create. A second save that POSTed again would archive
   * the plan just made and create another — harmless to data, and wrong.
   */
  it('stays on the screen after a create, says so, and PUTs the next save', async () => {
    await create(null);

    await setValue('#plan-name', 'Masa - jesień');
    await pick('Przysiad ze sztangą');
    await submit();

    (
      await vi.waitFor(() =>
        controller.expectOne((r) => r.url === '/api/trainer/plans' && r.method === 'POST'),
      )
    ).flush(PLAN);
    await settle();

    expect(lastToast()).toMatchObject({ tone: 'success', message: 'Plan zapisany.' });
    expect(submitLabel()).toBe('Zapisz zmiany');

    await submit();

    const second = await vi.waitFor(() =>
      controller.expectOne((r) => r.url === '/api/trainer/plans/p1' && r.method === 'PUT'),
    );
    expect(sentBody(second).memberId).toBe('m1');
    second.flush(PLAN);
  });

  /**
   * The router REUSES the component when only `:id` changes. The screen must follow the URL to the
   * next member — otherwise it shows one member's plan under another's URL and saves it with the
   * wrong id, which `member_changed` cannot catch.
   */
  it('follows the URL to another member instead of keeping the previous plan', async () => {
    await create(PLAN);
    expect(submitLabel()).toBe('Zapisz zmiany');

    params.next(convertToParamMap({ id: ACCOUNTLESS.id }));
    (
      await vi.waitFor(() => controller.expectOne(`/api/trainer/members/${ACCOUNTLESS.id}/plan`))
    ).flush({ member: ACCOUNTLESS, plan: null });
    await settle();

    expect(root().querySelector('h1')!.textContent).toContain('Plan — Piotr Nowak');
    expect(itemNames()).toEqual([]);
    expect(root().querySelector<HTMLInputElement>('#plan-name')!.value).toBe('');
    expect(submitLabel()).toBe('Przypisz plan');

    await setValue('#plan-name', 'Siła');
    await pick('Martwy ciąg');
    await submit();

    const post = await vi.waitFor(() =>
      controller.expectOne((r) => r.url === '/api/trainer/plans' && r.method === 'POST'),
    );
    expect(sentBody(post).memberId).toBe(ACCOUNTLESS.id);
    post.flush({ ...PLAN, id: 'p2', memberId: ACCOUNTLESS.id });
  });

  /** A member that does not exist is a screen state (outlet 4) — never a toast. */
  it('shows the not-found state when the member does not exist', async () => {
    configure('missing');

    (await vi.waitFor(() => controller.expectOne('/api/admin/exercises'))).flush(LIBRARY);
    (await vi.waitFor(() => controller.expectOne('/api/trainer/members/missing/plan'))).flush(
      null,
      {
        status: 404,
        statusText: 'Not Found',
      },
    );
    await settle();

    expect(html()).toContain('Nie znaleziono tych danych');
    expect(root().querySelector('form')).toBeNull();
    expect(lastToast()).toBeUndefined();
  });

  it('offers a retry when the member plan cannot be loaded', async () => {
    configure('m1');

    (await vi.waitFor(() => controller.expectOne('/api/admin/exercises'))).flush(LIBRARY);
    (await vi.waitFor(() => controller.expectOne('/api/trainer/members/m1/plan'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();

    expect(html()).toContain('Nie udało się wczytać planu tej osoby');

    root().querySelector<HTMLButtonElement>('.alert .link-button')!.click();
    (await vi.waitFor(() => controller.expectOne('/api/trainer/members/m1/plan'))).flush({
      member: MEMBER,
      plan: PLAN,
    });
    await settle();

    expect(itemNames()).toEqual(['Przysiad ze sztangą', 'Wyciskanie leżąc']);
  });

  /**
   * With the member fixed by the URL there is no control to name, so every member refusal lands in
   * the form banner (outlet 2).
   */
  it.each([
    ['member_changed', 'Ten plan należy do innego członka'],
    ['member_not_active', 'Tej osobie nie można teraz przypisać planu'],
  ])('shows %s in the form banner', async (reason, text) => {
    await create(PLAN);
    await submit();

    (await vi.waitFor(() => controller.expectOne('/api/trainer/plans/p1'))).flush(
      { reason },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(root().querySelector('form .alert')!.textContent).toContain(text);
  });

  /** One builder, mounted twice: "back" and "cancel" return to the list this mount came from. */
  it.each(['/admin/members', '/trainer/members'])(
    'returns to %s from the mount that names it',
    async (membersLink) => {
      await create(PLAN, { membersLink });

      const back = root().querySelector<HTMLAnchorElement>('.builder-back a')!;
      expect(back.textContent?.trim()).toBe('Wróć do listy członków');
      expect(back.getAttribute('href')).toBe(membersLink);
      expect(
        root().querySelector<HTMLAnchorElement>('.builder-actions a')!.getAttribute('href'),
      ).toBe(membersLink);
    },
  );

  /** An empty plan is refused by the server too (`no_items`); saying so here costs no round trip. */
  it('refuses to submit an empty plan without calling the API', async () => {
    await create(null);

    await setValue('#plan-name', 'Pusty');
    await submit();

    expect(html()).toContain('Dodaj przynajmniej jedno ćwiczenie');
    controller.expectNone('/api/trainer/plans');
  });

  it('puts a name refusal on the name control', async () => {
    await create(PLAN);
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
    await create(PLAN);
    await submit();

    (await vi.waitFor(() => controller.expectOne('/api/trainer/plans/p1'))).flush(
      { reason: 'conflict' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(html()).toContain('Odśwież stronę i spróbuj ponownie');
  });
});
