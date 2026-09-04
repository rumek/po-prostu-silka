import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { ExerciseSummary } from '../../../core/training/exercise.models';
import { ExerciseDetail } from './exercise-detail';

const FULL: ExerciseSummary = {
  id: 'e1',
  name: 'Przysiad ze sztangą',
  description: 'Podstawowe ćwiczenie na nogi.',
  muscleGroup: 'nogi',
  difficulty: 'średnie',
  equipment: 'sztanga, stojaki',
  preparation: 'Ustaw gryf na wysokości barków.',
  startingPosition: 'Stopy na szerokość bioder.',
  execution: 'Schodź kontrolowanie do kąta prostego.',
  videoId: 'dQw4w9-gX_Q',
  isActive: true,
  createdAt: new Date('2026-09-01T10:00').toISOString(),
};

/** A name and nothing else — the entry FR-018's optional fields make legitimate. */
const BARE: ExerciseSummary = {
  ...FULL,
  description: null,
  muscleGroup: null,
  difficulty: null,
  equipment: null,
  preparation: null,
  startingPosition: null,
  execution: null,
  videoId: null,
};

describe('ExerciseDetail', () => {
  let fixture: ComponentFixture<ExerciseDetail>;
  let controller: HttpTestingController;

  async function createWith(
    body: ExerciseSummary | null,
    options?: { status: number; statusText: string },
  ) {
    TestBed.configureTestingModule({
      imports: [ExerciseDetail],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['id', 'e1']]) } },
        },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ExerciseDetail);

    (await vi.waitFor(() => controller.expectOne('/api/admin/exercises/e1'))).flush(body, options);
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

  function iframe(): HTMLIFrameElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector('iframe');
  }

  function headings(): string[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('h2')).map(
      (h) => h.textContent?.trim() ?? '',
    );
  }

  it('renders every field an exercise has', async () => {
    await createWith(FULL);

    expect(html()).toContain('Przysiad ze sztangą');
    expect(html()).toContain('nogi');
    expect(html()).toContain('średnie');
    expect(html()).toContain('sztanga, stojaki');
    expect(html()).toContain('Ustaw gryf na wysokości barków.');
    expect(html()).toContain('Stopy na szerokość bioder.');
    expect(html()).toContain('Schodź kontrolowanie do kąta prostego.');
    expect(headings()).toEqual(['Opis', 'Przygotowanie', 'Pozycja startowa', 'Wykonanie']);
  });

  /**
   * An exercise with a name and nothing else is legitimate, so the absent sections are OMITTED —
   * eight empty headings would make a valid entry look broken.
   */
  it('omits the sections an exercise does not have', async () => {
    await createWith(BARE);

    expect(headings()).toEqual([]);
    expect(html()).toContain('ma na razie tylko nazwę');
  });

  /**
   * The player URL is composed from the stored id and passed through the sanitizer. Angular strips
   * an untrusted resource URL entirely, so asserting the src proves the trust step happened.
   */
  it('embeds the privacy-preserving player when there is a video', async () => {
    await createWith(FULL);

    expect(iframe()!.getAttribute('src')).toBe(
      'https://www.youtube-nocookie.com/embed/dQw4w9-gX_Q',
    );
    expect(iframe()!.getAttribute('title')).toContain('Przysiad ze sztangą');
  });

  /** No video renders no frame at all — an empty player reads as a failure. */
  it('renders no player when there is no video', async () => {
    await createWith(BARE);

    expect(iframe()).toBeNull();
  });

  /**
   * THE GUARD IN FRONT OF THE APP'S ONLY bypassSecurityTrustResourceUrl CALL.
   *
   * The server already guarantees a stored videoId matches ^[A-Za-z0-9_-]{11}$, so these bodies
   * cannot occur in practice — which is exactly why the re-check is easy to delete as redundant
   * during a later refactor. This pins it: whatever arrives in the response, a value that is not an
   * id renders no iframe rather than being trusted.
   */
  it.each([
    ['javascript:alert(1)'],
    ['https://evil.example/x'],
    ['dQw4w9-gX_QQ'],
    ['dQw4w9-gX_'],
    ['dQw4w9 gX_Q'],
  ])('refuses to embed a videoId that is not an id: %s', async (videoId) => {
    await createWith({ ...FULL, videoId });

    expect(iframe()).toBeNull();
  });

  /** A 404 is its own state: retrying the same id cannot help, so it offers the way back instead. */
  it('shows a not-found state for an unknown id', async () => {
    await createWith(null, { status: 404, statusText: 'Not Found' });

    expect(html()).toContain('Nie znaleziono takiego ćwiczenia');
    expect(html()).not.toContain('Spróbuj ponownie');
  });

  /** Any other failure is worth retrying, and says so. */
  it('offers a retry for a server failure', async () => {
    await createWith(null, { status: 500, statusText: 'Server Error' });

    expect(html()).toContain('Nie udało się wczytać');

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.link-button')!
      .click();
    (await vi.waitFor(() => controller.expectOne('/api/admin/exercises/e1'))).flush(FULL);
    await settle();

    expect(html()).toContain('Przysiad ze sztangą');
  });

  it('links to the edit screen for this exercise', async () => {
    await createWith(FULL);

    const edit = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      'a.button',
    );

    expect(edit!.getAttribute('href')).toBe('/admin/exercises/e1/edit');
  });

  it('marks a deactivated exercise as such', async () => {
    await createWith({ ...FULL, isActive: false });

    expect(html()).toContain('Nieaktywne');
  });
});
