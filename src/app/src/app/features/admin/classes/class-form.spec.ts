import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { ScheduledClass } from '../../../core/scheduling/class.models';
import { ClassForm } from './class-form';

const EXISTING: ScheduledClass = {
  id: 'c1',
  name: 'Joga',
  startsAt: new Date('2026-09-04T22:00').toISOString(),
  durationMinutes: 60,
  room: 'Sala A',
  instructor: 'Ola',
  capacity: 20,
  freeSpots: 20,
  status: 'Scheduled',
};

describe('ClassForm', () => {
  let fixture: ComponentFixture<ClassForm>;
  let controller: HttpTestingController;

  /** Boots the form with or without an :id route parameter. */
  async function create(id: string | null) {
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

    if (id) {
      (await vi.waitFor(() => controller.expectOne('/api/admin/classes'))).flush([EXISTING]);
    }

    await settle();
  }

  afterEach(() => controller.verify());

  /**
   * Drained twice: loading an existing class goes through getById -> getAdminClasses -> find, so
   * the promise chain needs more than one turn before the form is populated and rendered.
   */
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

  function set(id: string, value: string) {
    const control = input(id);
    control.value = value;
    control.dispatchEvent(new Event('input'));
  }

  function submit() {
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
  }

  async function fillValid() {
    set('name', 'Joga');
    set('startsAt', '2026-12-01T18:00');
    set('durationMinutes', '60');
    set('room', 'Sala A');
    set('instructor', 'Ola');
    set('capacity', '15');
    await settle();
  }

  it('renders an empty create form when there is no route id', async () => {
    await create(null);

    expect(el().textContent).toContain('Nowe zajęcia');
    expect(input('name').value).toBe('');
  });

  it('loads the existing class when the route carries an id', async () => {
    await create('c1');

    expect(el().textContent).toContain('Edytuj zajęcia');
    expect(input('name').value).toBe('Joga');
    expect(input('room').value).toBe('Sala A');
  });

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

  /** Server refusals land on the control responsible, not on a banner the admin has to interpret. */
  it('puts a room conflict on the room field', async () => {
    await create(null);
    await fillValid();
    submit();

    (await vi.waitFor(() => controller.expectOne('/api/admin/classes'))).flush(
      { reason: 'room_conflict' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(input('room').getAttribute('aria-invalid')).toBe('true');
    expect(el().textContent).toContain('sala jest już zajęta');
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

  it('PUTs to the member-scoped path when editing', async () => {
    await create('c1');
    submit();

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/classes/c1'));
    expect(request.request.method).toBe('PUT');
    request.flush({ ...EXISTING });
    await settle();
  });
});
