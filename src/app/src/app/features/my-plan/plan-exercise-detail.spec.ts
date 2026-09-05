import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { ExerciseSummary } from '../../core/training/exercise.models';
import { PlanExerciseDetail } from './plan-exercise-detail';

const URL = '/api/plans/mine/exercises/e1';

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

describe('PlanExerciseDetail', () => {
  let fixture: ComponentFixture<PlanExerciseDetail>;
  let controller: HttpTestingController;

  async function createWith(
    body: ExerciseSummary | null,
    options?: { status: number; statusText: string },
  ) {
    TestBed.configureTestingModule({
      imports: [PlanExerciseDetail],
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
    fixture = TestBed.createComponent(PlanExerciseDetail);

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

  function iframe(): HTMLIFrameElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector('iframe');
  }

  function headings(): string[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('h2')).map(
      (h) => h.textContent?.trim() ?? '',
    );
  }

  /**
   * READS THROUGH THE MEMBER'S OWN PLAN, not the admin library. The URL carries no member id — the
   * server takes that from the cookie — so this assertion is what pins the screen to the scoped
   * endpoint rather than to /api/admin/exercises, which a member cannot reach at all.
   */
  it('reads the exercise through the member plan endpoint', async () => {
    await createWith(FULL);

    expect(html()).toContain('Przysiad ze sztangą');
    expect(headings()).toEqual(['Opis', 'Przygotowanie', 'Pozycja startowa', 'Wykonanie']);
  });

  it('omits the sections an exercise does not have', async () => {
    await createWith(BARE);

    expect(headings()).toEqual([]);
    expect(html()).toContain('nie dodano jeszcze opisu ani filmu');
  });

  it('embeds the privacy-preserving player when there is a video', async () => {
    await createWith(FULL);

    expect(iframe()!.getAttribute('src')).toBe(
      'https://www.youtube-nocookie.com/embed/dQw4w9-gX_Q',
    );
    expect(iframe()!.getAttribute('title')).toContain('Przysiad ze sztangą');
  });

  it('renders no player when there is no video', async () => {
    await createWith(BARE);

    expect(iframe()).toBeNull();
  });

  /**
   * THE GUARD IN FRONT OF THIS COMPONENT'S ONLY bypassSecurityTrustResourceUrl CALL — carried over
   * from exercise-detail.spec.ts, whose review (exercise-library F4) required it.
   *
   * Copying the component without copying this test is exactly how the re-check would be lost: the
   * server already guarantees the stored id matches ^[A-Za-z0-9_-]{11}$, which makes the client-side
   * guard look redundant to a later refactor. This pins it on BOTH screens.
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

  /**
   * 404 means "not in your plan" as often as "no such exercise" — the join to the member's active
   * plan IS the authorization on that endpoint. Either way retrying cannot help, so no retry.
   */
  it('shows a not-in-your-plan state for a 404', async () => {
    await createWith(null, { status: 404, statusText: 'Not Found' });

    expect(html()).toContain('nie ma w Twoim planie');
    expect(html()).not.toContain('Spróbuj ponownie');
  });

  it('offers a retry for a server failure', async () => {
    await createWith(null, { status: 500, statusText: 'Server Error' });

    expect(html()).toContain('Nie udało się wczytać');

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.link-button')!
      .click();
    (await vi.waitFor(() => controller.expectOne(URL))).flush(FULL);
    await settle();

    expect(html()).toContain('Przysiad ze sztangą');
  });

  /**
   * A member does not maintain the library, and an exercise retired AFTER it was prescribed still
   * belongs in their plan — the server deliberately does not filter on IsActive here. So there is no
   * "Nieaktywne" badge and no edit link: both would be admin chrome on a member's screen.
   */
  it('shows no activation badge and no edit link', async () => {
    await createWith({ ...FULL, isActive: false });

    expect(html()).not.toContain('Nieaktywne');
    expect(html()).not.toContain('Edytuj');
    expect(html()).toContain('Przysiad ze sztangą');
  });
});
