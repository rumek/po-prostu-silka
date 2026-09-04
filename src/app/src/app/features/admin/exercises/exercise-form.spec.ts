import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { ExerciseRequest, ExerciseSummary } from '../../../core/training/exercise.models';
import { ExerciseForm } from './exercise-form';

const EXISTING: ExerciseSummary = {
  id: 'e1',
  name: 'Przysiad ze sztangą',
  description: 'Podstawowe ćwiczenie na nogi.',
  muscleGroup: 'nogi',
  difficulty: 'średnie',
  equipment: 'sztanga',
  preparation: 'Ustaw gryf na wysokości barków.',
  startingPosition: 'Stopy na szerokość bioder.',
  execution: 'Schodź kontrolowanie.',
  videoId: 'dQw4w9-gX_Q',
  isActive: true,
  createdAt: new Date('2026-09-01T10:00').toISOString(),
};

const OTHER: ExerciseSummary = {
  ...EXISTING,
  id: 'e2',
  name: 'Martwy ciąg',
  muscleGroup: 'plecy',
  difficulty: 'trudne',
  videoId: null,
};

describe('ExerciseForm', () => {
  let fixture: ComponentFixture<ExerciseForm>;
  let controller: HttpTestingController;

  /** @param id null creates; a string edits that exercise. */
  async function create(id: string | null, library: ExerciseSummary[] = []) {
    TestBed.configureTestingModule({
      imports: [ExerciseForm],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['id', id]]) } },
        },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ExerciseForm);

    // The suggestions fetch fires on init regardless of create-vs-edit.
    (await vi.waitFor(() => controller.expectOne('/api/admin/exercises'))).flush(library);

    if (id) {
      (await vi.waitFor(() => controller.expectOne(`/api/admin/exercises/${id}`))).flush(EXISTING);
    }

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

  function input(id: string): HTMLInputElement | HTMLTextAreaElement {
    return (fixture.nativeElement as HTMLElement).querySelector<
      HTMLInputElement | HTMLTextAreaElement
    >(`#${id}`)!;
  }

  function type(id: string, value: string) {
    const el = input(id);
    el.value = value;
    el.dispatchEvent(new Event('input'));
  }

  function submit() {
    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit'));
  }

  it('sends only a name when nothing else is filled in, with every other field null', async () => {
    await create(null);

    type('name', 'Wiosłowanie');
    await settle();
    submit();

    const request = await vi.waitFor(() =>
      controller.expectOne((r) => r.method === 'POST' && r.url === '/api/admin/exercises'),
    );
    const body = request.request.body as ExerciseRequest;

    expect(body.name).toBe('Wiosłowanie');
    expect(body.description).toBeNull();
    expect(body.muscleGroup).toBeNull();
    expect(body.execution).toBeNull();
    expect(body.videoUrl).toBeNull();

    request.flush({ ...EXISTING, name: 'Wiosłowanie' });
  });

  /**
   * The stored form is a bare id; the form shows the canonical watch URL so the field reads like a
   * link. Saving it back unchanged must round-trip to the same video.
   */
  it('shows the stored video as a canonical watch URL when editing', async () => {
    await create('e1');

    expect((input('videoUrl') as HTMLInputElement).value).toBe(
      'https://www.youtube.com/watch?v=dQw4w9-gX_Q',
    );
    expect((input('name') as HTMLInputElement).value).toBe('Przysiad ze sztangą');
    expect(input('execution').value).toBe('Schodź kontrolowanie.');
  });

  /** Suggestions come from the library itself — no controlled vocabulary, no second endpoint. */
  it('offers the muscle groups and difficulties already used in the library', async () => {
    await create(null, [EXISTING, OTHER]);

    const options = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLOptionElement>(
        '#muscleGroupOptions option',
      ),
    ).map((o) => o.value);

    expect(options).toEqual(['nogi', 'plecy']);

    const difficulties = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLOptionElement>(
        '#difficultyOptions option',
      ),
    ).map((o) => o.value);

    expect(difficulties).toEqual(['średnie', 'trudne']);
  });

  /** A failed suggestions fetch is a form without suggestions, never a broken form. */
  it('stays usable when the suggestions fetch fails', async () => {
    TestBed.configureTestingModule({
      imports: [ExerciseForm],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map() } } },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ExerciseForm);

    (await vi.waitFor(() => controller.expectOne('/api/admin/exercises'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();

    expect(input('name')).not.toBeNull();
    expect(html()).not.toContain('Nie udało się');
  });

  it('puts a name collision on the name field, not in a banner', async () => {
    await create(null);

    type('name', 'Przysiad ze sztangą');
    await settle();
    submit();

    (
      await vi.waitFor(() =>
        controller.expectOne((r) => r.method === 'POST' && r.url === '/api/admin/exercises'),
      )
    ).flush({ reason: 'name_taken' }, { status: 409, statusText: 'Conflict' });
    await settle();

    expect(html()).toContain('Aktywne ćwiczenie o tej nazwie już istnieje');
    expect(input('name').getAttribute('aria-invalid')).toBe('true');
  });

  /**
   * The video field is the one control with no client-side pattern — the server owns what a YouTube
   * link is — so its refusal MUST be routed back to the field, or the admin gets a banner and nine
   * fields to guess between.
   */
  it('puts an unusable video link on the video field', async () => {
    await create(null);

    type('name', 'Wiosłowanie');
    type('videoUrl', 'https://vimeo.com/123456789');
    await settle();
    submit();

    (
      await vi.waitFor(() =>
        controller.expectOne((r) => r.method === 'POST' && r.url === '/api/admin/exercises'),
      )
    ).flush({ reason: 'invalid_video_url' }, { status: 400, statusText: 'Bad Request' });
    await settle();

    expect(html()).toContain('To nie wygląda na link do filmu na YouTube');
    expect(input('videoUrl').getAttribute('aria-invalid')).toBe('true');
  });

  it('refuses to submit without a name', async () => {
    await create(null);

    submit();
    await settle();

    controller.expectNone((r) => r.method === 'POST');
    expect(html()).toContain('Podaj nazwę ćwiczenia');
  });

  it('edits through PUT rather than POST', async () => {
    await create('e1');

    type('description', 'Nowy opis.');
    await settle();
    submit();

    const request = await vi.waitFor(() =>
      controller.expectOne((r) => r.method === 'PUT' && r.url === '/api/admin/exercises/e1'),
    );

    expect((request.request.body as ExerciseRequest).description).toBe('Nowy opis.');

    request.flush(EXISTING);
  });
});
