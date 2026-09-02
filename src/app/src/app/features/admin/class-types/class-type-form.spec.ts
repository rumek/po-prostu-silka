import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { ClassTypeSummary } from '../../../core/scheduling/class-type.models';
import { ClassTypeForm } from './class-type-form';

const EXISTING: ClassTypeSummary = {
  id: 't1',
  name: 'Joga dla początkujących',
  description: 'Spokojne zajęcia dla osób bez doświadczenia.',
  defaultDurationMinutes: 60,
  defaultCapacity: 12,
  isActive: true,
  createdAt: new Date('2026-09-01T10:00').toISOString(),
};

describe('ClassTypeForm', () => {
  let fixture: ComponentFixture<ClassTypeForm>;
  let controller: HttpTestingController;

  /** Boots the form with or without an :id route parameter. */
  async function create(id: string | null) {
    TestBed.configureTestingModule({
      imports: [ClassTypeForm],
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
    fixture = TestBed.createComponent(ClassTypeForm);
    fixture.detectChanges();

    if (id) {
      (await vi.waitFor(() => controller.expectOne(`/api/admin/class-types/${id}`))).flush(
        EXISTING,
      );
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

  function field(id: string): HTMLInputElement | HTMLTextAreaElement {
    return el().querySelector<HTMLInputElement | HTMLTextAreaElement>(`#${id}`)!;
  }

  function set(id: string, value: string) {
    const control = field(id);
    control.value = value;
    control.dispatchEvent(new Event('input'));
  }

  function submit() {
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
  }

  async function fillValid() {
    set('name', 'Pilates');
    set('defaultDurationMinutes', '45');
    set('defaultCapacity', '10');
    await settle();
  }

  it('renders an empty create form when there is no route id', async () => {
    await create(null);

    expect(el().textContent).toContain('Nowy typ zajęć');
    expect(field('name').value).toBe('');
  });

  it('loads the existing type when the route carries an id', async () => {
    await create('t1');

    expect(el().textContent).toContain('Edytuj typ zajęć');
    expect(field('name').value).toBe('Joga dla początkujących');
    expect(field('description').value).toBe('Spokojne zajęcia dla osób bez doświadczenia.');
    expect(field('defaultDurationMinutes').value).toBe('60');
    expect(field('defaultCapacity').value).toBe('12');
  });

  /** The API's "absent" is null; the form's is an empty string. Feeding null in would render "null". */
  it('renders a null description as an empty field', async () => {
    TestBed.configureTestingModule({
      imports: [ClassTypeForm],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 't1' } } } },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ClassTypeForm);
    fixture.detectChanges();

    (await vi.waitFor(() => controller.expectOne('/api/admin/class-types/t1'))).flush({
      ...EXISTING,
      description: null,
    });
    await settle();

    expect(field('description').value).toBe('');
  });

  it('does not submit an invalid form', async () => {
    await create(null);

    set('name', '');
    submit();
    await settle();

    // afterEach's controller.verify() proves no request was issued.
    expect(el().querySelectorAll('.field-error').length).toBeGreaterThan(0);
  });

  /** The bounds mirror the server's, so an obvious typo never becomes a round trip. */
  it('refuses an out-of-range duration and capacity client-side', async () => {
    await create(null);

    set('name', 'Pilates');
    set('defaultDurationMinutes', '481');
    set('defaultCapacity', '201');
    await settle();

    submit();
    await settle();

    expect(el().textContent).toContain('1–480');
    expect(el().textContent).toContain('1–200');
  });

  it('sends a blank description as null, not as an empty string', async () => {
    await create(null);
    await fillValid();

    submit();

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/class-types'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body.description).toBeNull();

    request.flush({ ...EXISTING });
    await settle();
  });

  it('trims the description before sending it', async () => {
    await create(null);
    await fillValid();
    set('description', '  Zajęcia wzmacniające.  ');
    await settle();

    submit();

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/class-types'));
    expect(request.request.body.description).toBe('Zajęcia wzmacniające.');

    request.flush({ ...EXISTING });
    await settle();
  });

  /** Server refusals land on the control responsible, not on a banner the admin has to interpret. */
  it('puts a taken name on the name field', async () => {
    await create(null);
    await fillValid();
    submit();

    (await vi.waitFor(() => controller.expectOne('/api/admin/class-types'))).flush(
      { reason: 'name_taken' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(field('name').getAttribute('aria-invalid')).toBe('true');
    expect(el().textContent).toContain('Aktywny typ o tej nazwie już istnieje');
    expect(el().querySelector('.alert')).toBeNull();
  });

  it('puts an over-long description on the description field', async () => {
    await create(null);
    await fillValid();
    submit();

    (await vi.waitFor(() => controller.expectOne('/api/admin/class-types'))).flush(
      { reason: 'description_too_long' },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(field('description').getAttribute('aria-invalid')).toBe('true');
    expect(el().textContent).toContain('najwyżej 1000 znaków');
    expect(el().querySelector('.alert')).toBeNull();
  });

  it('falls back to a form-level message for an unexpected failure', async () => {
    await create(null);
    await fillValid();
    submit();

    (await vi.waitFor(() => controller.expectOne('/api/admin/class-types'))).flush(null, {
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

    (await vi.waitFor(() => controller.expectOne('/api/admin/class-types'))).flush({ ...EXISTING });
    await settle();

    expect(navigate).toHaveBeenCalledWith(['/admin/class-types']);
  });

  it('PUTs to the type-scoped path when editing', async () => {
    await create('t1');
    submit();

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/class-types/t1'));
    expect(request.request.method).toBe('PUT');
    request.flush({ ...EXISTING });
    await settle();
  });
});
