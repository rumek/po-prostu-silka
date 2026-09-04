import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ExerciseSummary } from '../../../core/training/exercise.models';
import { Exercises } from './exercises';

const PRZYSIAD: ExerciseSummary = {
  id: 'e1',
  name: 'Przysiad ze sztangą',
  description: 'Podstawowe ćwiczenie na nogi.',
  muscleGroup: 'nogi',
  difficulty: 'średnie',
  equipment: 'sztanga, stojaki',
  preparation: null,
  startingPosition: null,
  execution: null,
  videoId: 'dQw4w9-gX_Q',
  isActive: true,
  createdAt: new Date('2026-09-01T10:00').toISOString(),
};

const BEZ_FILMU: ExerciseSummary = {
  ...PRZYSIAD,
  id: 'e2',
  name: 'Martwy ciąg',
  description: null,
  muscleGroup: 'plecy',
  videoId: null,
};

const RETIRED: ExerciseSummary = {
  ...PRZYSIAD,
  id: 'e3',
  name: 'Wyciskanie francuskie',
  videoId: null,
  isActive: false,
};

describe('Exercises', () => {
  let fixture: ComponentFixture<Exercises>;
  let controller: HttpTestingController;

  async function createWith(rows: ExerciseSummary[]) {
    TestBed.configureTestingModule({
      imports: [Exercises],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Exercises);

    (await vi.waitFor(() => controller.expectOne('/api/admin/exercises'))).flush(rows);
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

  function rows(): HTMLElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.exercises-row'));
  }

  function images(): HTMLImageElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('img'));
  }

  function buttonIn(row: HTMLElement, label: string): HTMLButtonElement {
    return Array.from(row.querySelectorAll('button')).find((b) =>
      (b.textContent ?? '').includes(label),
    )!;
  }

  function toggle(): HTMLInputElement {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      '.exercises-toggle input',
    )!;
  }

  it('renders one row per exercise, with its muscle group and description', async () => {
    await createWith([PRZYSIAD]);

    expect(rows().length).toBe(1);
    expect(html()).toContain('Przysiad ze sztangą');
    expect(html()).toContain('nogi');
    expect(html()).toContain('Podstawowe ćwiczenie na nogi.');
  });

  /**
   * The thumbnail is composed from the stored id, never sent by the API — this pins that the
   * composition happens and that it uses the shape YouTube actually serves for every video.
   */
  it('shows a YouTube thumbnail for an exercise that has a video', async () => {
    await createWith([PRZYSIAD]);

    expect(images().length).toBe(1);
    expect(images()[0].getAttribute('src')).toBe(
      'https://img.youtube.com/vi/dQw4w9-gX_Q/mqdefault.jpg',
    );
  });

  /** No video renders the placeholder box, not a broken image. */
  it('renders no image for an exercise without a video', async () => {
    await createWith([BEZ_FILMU]);

    expect(images().length).toBe(0);
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('.exercises-thumbnail-empty'),
    ).not.toBeNull();
  });

  /**
   * YouTube serves these images, so a deleted video 404s outside our control. The placeholder must
   * take over rather than leaving a broken-image icon in the list.
   */
  it('falls back to the placeholder when a thumbnail fails to load', async () => {
    await createWith([PRZYSIAD]);

    images()[0].dispatchEvent(new Event('error'));
    await settle();

    expect(images().length).toBe(0);
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('.exercises-thumbnail-empty'),
    ).not.toBeNull();
  });

  /** The toggle is off by default: retired exercises are the exception and must not crowd the list. */
  it('hides inactive exercises until the toggle is set', async () => {
    await createWith([PRZYSIAD, RETIRED]);

    expect(rows().length).toBe(1);
    expect(html()).not.toContain('Wyciskanie francuskie');

    toggle().click();
    await settle();

    expect(rows().length).toBe(2);
    expect(html()).toContain('Wyciskanie francuskie');
    expect(html()).toContain('Nieaktywne');
  });

  it('shows the empty state when the library has no exercises', async () => {
    await createWith([]);

    expect(html()).toContain('Nie dodano jeszcze żadnego ćwiczenia');
  });

  it('distinguishes "all hidden by the filter" from "none yet"', async () => {
    await createWith([RETIRED]);

    expect(html()).toContain('Wszystkie ćwiczenia są nieaktywne');
  });

  it('surfaces a failed load and retries', async () => {
    TestBed.configureTestingModule({
      imports: [Exercises],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Exercises);

    (await vi.waitFor(() => controller.expectOne('/api/admin/exercises'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();

    expect(html()).toContain('Nie udało się wczytać ćwiczeń');

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.link-button')!
      .click();
    (await vi.waitFor(() => controller.expectOne('/api/admin/exercises'))).flush([PRZYSIAD]);
    await settle();

    expect(html()).toContain('Przysiad ze sztangą');
  });

  it('deactivates a row and says where it went', async () => {
    await createWith([PRZYSIAD]);

    buttonIn(rows()[0], 'Dezaktywuj').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/exercises/e1/deactivate'))).flush({
      ...PRZYSIAD,
      isActive: false,
    });
    await settle();

    expect(html()).toContain('został');
    expect(html()).toContain('Pokaż nieaktywne');
    expect(rows().length).toBe(0);
  });

  /**
   * Activation is the one action refused for a reason the admin can fix, and it carries no name to
   * attach the message to — so it has to be explained at list level rather than swallowed.
   */
  it('explains a name collision when reactivating', async () => {
    await createWith([RETIRED]);

    toggle().click();
    await settle();

    buttonIn(rows()[0], 'Aktywuj').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/exercises/e3/activate'))).flush(
      { reason: 'name_taken' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(html()).toContain('jest teraz zajęta');
  });

  it('links each row to its edit screen', async () => {
    await createWith([PRZYSIAD]);

    const link = rows()[0].querySelector('a')!;

    expect(link.getAttribute('href')).toBe('/admin/exercises/e1/edit');
  });
});
