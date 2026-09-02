import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { TrainerSummary } from '../../../core/admin/member-admin.models';
import { ScheduledClass } from '../../../core/scheduling/class.models';
import { ClassTypeSummary } from '../../../core/scheduling/class-type.models';
import { ClassForm } from './class-form';

const YOGA: ClassTypeSummary = {
  id: 't1',
  name: 'Joga',
  description: 'Dla początkujących',
  defaultDurationMinutes: 75,
  defaultCapacity: 18,
  isActive: true,
  createdAt: '2026-09-01T10:00:00Z',
};

const RETIRED: ClassTypeSummary = {
  ...YOGA,
  id: 't2',
  name: 'Pilates',
  isActive: false,
};

const TRAINERS: TrainerSummary[] = [
  { id: 'u1', displayName: 'Ola' },
  { id: 'u2', displayName: 'Marek' },
];

const EXISTING: ScheduledClass = {
  id: 'c1',
  classTypeId: 't1',
  name: 'Joga',
  description: 'Dla początkujących',
  startsAt: new Date('2026-09-04T22:00').toISOString(),
  durationMinutes: 60,
  instructorUserId: 'u1',
  instructor: 'Ola',
  // Deliberately DIFFERENT from YOGA's defaults (75/18) — several tests below turn on the fact that
  // an occurrence keeps its own numbers rather than re-deriving them from the type.
  capacity: 20,
  freeSpots: 20,
  status: 'Scheduled',
};

describe('ClassForm', () => {
  let fixture: ComponentFixture<ClassForm>;
  let controller: HttpTestingController;

  /**
   * Boots the form with or without an :id route parameter.
   *
   * The two lookup requests are flushed first because the component awaits both before it renders
   * anything — including the empty states.
   */
  async function create(
    id: string | null,
    options: { types?: ClassTypeSummary[]; trainers?: TrainerSummary[] } = {},
  ) {
    TestBed.configureTestingModule({
      imports: [ClassForm],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => id } } },
        },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ClassForm);
    fixture.detectChanges();

    (await vi.waitFor(() => controller.expectOne('/api/admin/class-types'))).flush(
      options.types ?? [YOGA, RETIRED],
    );
    (await vi.waitFor(() => controller.expectOne('/api/admin/trainers'))).flush(
      options.trainers ?? TRAINERS,
    );

    if (id) {
      // A dedicated single-item GET, not a filtered list fetch — see class.service.getById.
      (await vi.waitFor(() => controller.expectOne(`/api/admin/classes/${id}`))).flush(EXISTING);
    }

    await settle();
  }

  afterEach(() => controller.verify());

  /** Drained twice so the load promise settles and the populated form renders before assertions. */
  async function settle() {
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function input(id: string): HTMLInputElement {
    return el().querySelector<HTMLInputElement>(`#${id}`)!;
  }

  function select(id: string): HTMLSelectElement {
    return el().querySelector<HTMLSelectElement>(`#${id}`)!;
  }

  function set(id: string, value: string) {
    const control = input(id);
    control.value = value;
    control.dispatchEvent(new Event('input'));
  }

  /** A select needs BOTH events: change drives the prefill, input drives the reactive form. */
  function pick(id: string, value: string) {
    const control = select(id);
    control.value = value;
    control.dispatchEvent(new Event('change'));
    control.dispatchEvent(new Event('input'));
  }

  function submit() {
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
  }

  async function fillValid() {
    pick('classTypeId', 't1');
    set('startsAt', '2026-12-01T18:00');
    pick('instructorUserId', 'u1');
    await settle();
  }

  it('renders an empty create form when there is no route id', async () => {
    await create(null);

    expect(el().textContent).toContain('Nowe zajęcia');
    expect(select('classTypeId').value).toBe('');
    expect(select('instructorUserId').value).toBe('');
  });

  it('offers only active class types when creating', async () => {
    await create(null);

    const labels = [...select('classTypeId').querySelectorAll('option')].map((o) =>
      o.textContent?.trim(),
    );

    expect(labels).toContain('Joga');
    expect(labels.some((label) => label?.startsWith('Pilates'))).toBe(false);
  });

  // --- the prefill, and the one case where it must NOT fire ------------------

  it('prefills duration and capacity from the chosen type', async () => {
    await create(null);

    pick('classTypeId', 't1');
    await settle();

    expect(input('durationMinutes').value).toBe('75');
    expect(input('capacity').value).toBe('18');
  });

  it('keeps an override the admin typed after the prefill', async () => {
    await create(null);

    pick('classTypeId', 't1');
    await settle();
    set('capacity', '8');
    await settle();

    expect(input('capacity').value).toBe('8');
  });

  /**
   * THE REGRESSION THIS FILE EXISTS FOR. An occurrence owns its own copies of duration and capacity
   * (prd-v2 FR-007). Re-deriving them from the type on load would silently replace an override —
   * and, for capacity, move the value the no-overbooking guarantee is checked against.
   */
  it('does NOT re-prefill the numbers when loading an existing class', async () => {
    await create('c1');

    expect(input('capacity').value).toBe('20');
    expect(input('durationMinutes').value).toBe('60');
    expect(input('capacity').value).not.toBe('18');
  });

  it('loads the existing class when the route carries an id', async () => {
    await create('c1');

    expect(el().textContent).toContain('Edytuj zajęcia');
    expect(select('classTypeId').value).toBe('t1');
    expect(select('instructorUserId').value).toBe('u1');
  });

  /**
   * styles.scss suppresses the native chevron with `appearance: none` and draws the replacement as
   * `.select::after` — a select is a replaced element and generates no pseudo-element of its own. A
   * select added without the wrapper therefore has NO arrow at all, and nothing else would catch it.
   */
  it('wraps every select so the custom chevron has somewhere to render', async () => {
    await create(null);

    const selects = [...el().querySelectorAll('select')];

    expect(selects.length).toBeGreaterThan(0);
    expect(selects.every((s) => s.parentElement?.classList.contains('select'))).toBe(true);
  });

  it('disables the type select when editing', async () => {
    await create('c1');

    expect(select('classTypeId').disabled).toBe(true);
    expect(el().textContent).toContain('Typu nie można zmienić');
  });

  // --- the empty states ------------------------------------------------------

  it('signposts the class-type screen when no active type exists', async () => {
    await create(null, { types: [RETIRED] });

    expect(el().textContent).toContain('Najpierw zdefiniuj typ zajęć');
    expect(el().querySelector('a[href="/admin/class-types"]')).not.toBeNull();
    expect(el().querySelector('form')).toBeNull();
  });

  it('signposts the members screen when nobody holds the trainer role', async () => {
    await create(null, { trainers: [] });

    expect(el().textContent).toContain('roli prowadzącego');
    expect(el().querySelector('a[href="/admin/members"]')).not.toBeNull();
    expect(el().querySelector('form')).toBeNull();
  });

  /**
   * Both empty states are CREATE-only preconditions. An existing class already has a type and an
   * instructor, so an empty list must not replace the form — that would lock the admin out of a
   * perfectly valid class they need to reschedule.
   */
  it('still renders the edit form when every trainer lost the role', async () => {
    await create('c1', { trainers: [] });

    expect(el().querySelector('form')).not.toBeNull();
    expect(el().textContent).not.toContain('roli prowadzącego');
    expect(input('capacity').value).toBe('20');
  });

  /**
   * `/api/admin/trainers` is active-only, so a trainer since blocked or de-roled is missing from it.
   * Without a fallback option the select renders blank while the control keeps its value — a
   * required field that looks unfilled, validates fine, and only fails on submit.
   */
  it('keeps the stored instructor selectable when they are no longer an active trainer', async () => {
    await create('c1', { trainers: [{ id: 'u2', displayName: 'Marek' }] });

    const options = [...select('instructorUserId').querySelectorAll('option')];
    const stale = options.find((o) => o.getAttribute('value') === 'u1');

    expect(stale).toBeDefined();
    expect(stale!.textContent).toContain('(nieaktywny)');
    expect(select('instructorUserId').value).toBe('u1');
  });

  it('still renders the edit form when every class type was retired', async () => {
    await create('c1', { types: [RETIRED] });

    expect(el().querySelector('form')).not.toBeNull();
    expect(el().textContent).not.toContain('Najpierw zdefiniuj typ zajęć');
  });

  // --- time handling ---------------------------------------------------------

  /**
   * The silent failure mode of this screen: datetime-local carries no timezone, so a UTC value fed
   * straight into it would display the wrong hour and save back shifted.
   */
  it('shows the start time as the local wall clock, not the UTC instant', async () => {
    await create('c1');

    // EXISTING was built from 22:00 local, so that is what the admin must see.
    expect(input('startsAt').value).toBe(`2026-09-04T22:00`);
  });

  it('sends the typed local time back as a UTC instant', async () => {
    await create(null);
    await fillValid();

    submit();

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/classes'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body.classTypeId).toBe('t1');
    expect(request.request.body.instructorUserId).toBe('u1');

    // Round-trips to the same wall clock the admin typed.
    const sent = new Date(request.request.body.startsAt as string);
    expect(sent.getHours()).toBe(18);
    expect(sent.getMinutes()).toBe(0);

    request.flush({ ...EXISTING });
    await settle();
  });

  it('does not submit an invalid form', async () => {
    await create(null);

    submit();
    await settle();

    // afterEach's controller.verify() proves no request was issued.
    expect(el().querySelectorAll('.field-error').length).toBeGreaterThan(0);
  });

  // --- server refusals land on the control responsible -----------------------

  it('puts a time conflict on the start field', async () => {
    await create(null);
    await fillValid();
    submit();

    (await vi.waitFor(() => controller.expectOne('/api/admin/classes'))).flush(
      { reason: 'time_conflict' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(input('startsAt').getAttribute('aria-invalid')).toBe('true');
    expect(el().textContent).toContain('odbywają się już inne zajęcia');
    expect(el().querySelector('.alert')).toBeNull();
  });

  it('puts a past start on the start field', async () => {
    await create(null);
    await fillValid();
    submit();

    (await vi.waitFor(() => controller.expectOne('/api/admin/classes'))).flush(
      { reason: 'starts_in_past' },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(input('startsAt').getAttribute('aria-invalid')).toBe('true');
    expect(el().textContent).toContain('w przeszłości');
    expect(el().querySelector('.alert')).toBeNull();
  });

  it('puts a stale trainer on the instructor field', async () => {
    await create(null);
    await fillValid();
    submit();

    (await vi.waitFor(() => controller.expectOne('/api/admin/classes'))).flush(
      { reason: 'instructor_not_trainer' },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(select('instructorUserId').getAttribute('aria-invalid')).toBe('true');
    expect(el().textContent).toContain('nie jest już aktywnym prowadzącym');
  });

  /** The type control is disabled while editing, so these carry a banner rather than a field error. */
  it('reports a rejected class type as a form-level message', async () => {
    await create(null);
    await fillValid();
    submit();

    (await vi.waitFor(() => controller.expectOne('/api/admin/classes'))).flush(
      { reason: 'inactive_class_type' },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(el().querySelector('.alert')?.textContent).toContain('Nie można użyć tego typu zajęć');
  });

  it('falls back to a form-level message for an unexpected failure', async () => {
    await create(null);
    await fillValid();
    submit();

    (await vi.waitFor(() => controller.expectOne('/api/admin/classes'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();

    expect(el().querySelector('.alert')).not.toBeNull();
  });

  it('navigates back to the list after a successful save', async () => {
    await create(null);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    await fillValid();
    submit();

    (await vi.waitFor(() => controller.expectOne('/api/admin/classes'))).flush({ ...EXISTING });
    await settle();

    expect(navigate).toHaveBeenCalledWith(['/admin/classes']);
  });

  /**
   * getRawValue, not value: the type control is DISABLED when editing, and `value` omits disabled
   * controls. The API requires classTypeId on an edit — it is how it detects an attempted change —
   * so dropping it would turn every edit into a missing_field.
   */
  it('PUTs the class type back even though its control is disabled', async () => {
    await create('c1');
    submit();

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/classes/c1'));
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.classTypeId).toBe('t1');
    request.flush({ ...EXISTING });
    await settle();
  });
});
