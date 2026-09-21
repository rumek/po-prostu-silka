import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TrainerMember, TrainerMemberPage } from '../../../core/training/training-plan.models';
import { TRAINER_SEARCH_DEBOUNCE_MS, TrainerMembers } from './trainer-members';

const ANNA: TrainerMember = {
  id: 'm1',
  displayName: 'Anna Kowalska',
  hasAccount: true,
  planName: 'Masa - jesień',
};

/** A person the club recorded who never registered (S-14), with no plan yet. */
const PIOTR: TrainerMember = {
  id: 'm2',
  displayName: 'Piotr Nowak',
  hasAccount: false,
  planName: null,
};

/** The first page, unsearched — what the screen asks for on a plain visit. */
const LIST = '/api/trainer/members?pageSize=25';

function page(items: TrainerMember[], total = items.length, pageNumber = 1): TrainerMemberPage {
  return { items, total, page: pageNumber, pageSize: 25 };
}

/** `count` distinct members — a full page, for the pager tests. */
function many(count: number, offset = 0): TrainerMember[] {
  return Array.from({ length: count }, (_, index) => ({
    ...ANNA,
    id: `p${offset + index}`,
    displayName: `Członek ${offset + index}`,
  }));
}

/**
 * The trainer's member list (S-22, UX-08). Templated on members.spec.ts, because the URL-state logic
 * is a recorded second copy of that screen's — so the same behaviours are pinned the same way.
 */
describe('TrainerMembers', () => {
  let fixture: ComponentFixture<TrainerMembers>;
  let controller: HttpTestingController;
  let router: Router;

  async function arrive(url = '/') {
    TestBed.configureTestingModule({
      imports: [TrainerMembers],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    controller = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);

    await router.navigateByUrl(url);
    fixture = TestBed.createComponent(TrainerMembers);
  }

  async function createWith(rows: TrainerMember[] | TrainerMemberPage, url = '/', request = LIST) {
    await arrive(url);

    (await vi.waitFor(() => controller.expectOne(request))).flush(
      Array.isArray(rows) ? page(rows) : rows,
    );
    await settle();
  }

  async function settle() {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => controller.verify());

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function html(): string {
    return root().textContent ?? '';
  }

  function rows(): HTMLElement[] {
    return Array.from(root().querySelectorAll('li'));
  }

  function type(value: string): void {
    const input = root().querySelector<HTMLInputElement>('input[type="search"]')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function pagerButton(label: string): HTMLButtonElement {
    return Array.from(
      root().querySelectorAll<HTMLButtonElement>('nav[aria-label="Strony listy członków"] button'),
    ).find((b) => (b.textContent ?? '').includes(label))!;
  }

  it('renders the name, "bez konta" and the plan name or "brak planu"', async () => {
    await createWith([ANNA, PIOTR]);

    expect(rows().length).toBe(2);

    expect(rows()[0].querySelector('.row-name')!.textContent).toContain('Anna Kowalska');
    expect(rows()[0].querySelector('.row-meta')!.textContent).toContain('Plan: Masa - jesień');
    expect(rows()[0].textContent).not.toContain('Bez konta');

    expect(rows()[1].querySelector('.row-meta')!.textContent).toContain('Bez konta');
    expect(rows()[1].querySelector('.row-meta')!.textContent).toContain('Brak planu');
  });

  it("links each row to that member's plan", async () => {
    await createWith([ANNA, PIOTR]);

    const links = rows().map((row) => row.querySelector('a')!.getAttribute('href'));
    expect(links).toEqual(['/trainer/members/m1/plan', '/trainer/members/m2/plan']);
  });

  /**
   * DEFENSIVE: the model has no e-mail and the API sends none, and this is what notices if a template
   * or a widened model ever starts rendering one.
   */
  it('renders no e-mail address', async () => {
    await createWith([{ ...ANNA, email: 'anna@test.local' } as TrainerMember]);

    expect(html()).not.toContain('@');
  });

  it('says the search found nobody, rather than that the club is empty', async () => {
    await createWith([], '/?q=zzz', '/api/trainer/members?search=zzz&pageSize=25');

    expect(html()).toContain('Brak członków pasujących do wyszukiwania');
  });

  /**
   * One request per pause, not per keystroke — and with no `page`: a new phrase is a new list, which
   * starts at the top. The phrase is written to the URL, replacing the history entry.
   */
  it('searches on the server once typing pauses, and puts the phrase in ?q=', async () => {
    await createWith([ANNA, PIOTR], '/?page=2', '/api/trainer/members?page=2&pageSize=25');
    const navigate = vi.spyOn(router, 'navigate');

    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    try {
      type('k');
      vi.advanceTimersByTime(TRAINER_SEARCH_DEBOUNCE_MS - 50);
      type('kow');
      vi.advanceTimersByTime(TRAINER_SEARCH_DEBOUNCE_MS - 1);

      controller.expectNone((request) => request.url === '/api/trainer/members');

      vi.advanceTimersByTime(1);
    } finally {
      vi.useRealTimers();
    }

    (
      await vi.waitFor(() => controller.expectOne('/api/trainer/members?search=kow&pageSize=25'))
    ).flush(page([ANNA]));
    await settle();

    expect(rows().length).toBe(1);
    expect(router.url).toBe('/?q=kow');
    expect(navigate.mock.calls.at(-1)?.[1]?.replaceUrl).toBe(true);
  });

  it('restores the phrase and the page from the URL', async () => {
    await createWith(
      page([ANNA], 26, 2),
      '/?q=kow&page=2',
      '/api/trainer/members?search=kow&page=2&pageSize=25',
    );

    expect(root().querySelector<HTMLInputElement>('input[type="search"]')!.value).toBe('kow');
  });

  it('hides the pager when everything fits on one page', async () => {
    await createWith([ANNA, PIOTR]);

    expect(root().querySelector('nav[aria-label="Strony listy członków"]')).toBeNull();
  });

  it('moves ?page= forward and back through the server', async () => {
    await createWith(page(many(25), 30));

    expect(html()).toContain('1–25 z 30');
    expect(pagerButton('Poprzednia').disabled).toBe(true);

    pagerButton('Następna').click();
    (await vi.waitFor(() => controller.expectOne('/api/trainer/members?page=2&pageSize=25'))).flush(
      page(many(5, 25), 30, 2),
    );
    await settle();

    expect(router.url).toBe('/?page=2');
    expect(html()).toContain('26–30 z 30');
    expect(pagerButton('Następna').disabled).toBe(true);

    pagerButton('Poprzednia').click();
    (await vi.waitFor(() => controller.expectOne(LIST))).flush(page(many(25), 30));
    await settle();

    expect(router.url).toBe('/');
  });

  it('lands on the last page when the requested one is past the end', async () => {
    await arrive('/?page=3');

    (await vi.waitFor(() => controller.expectOne('/api/trainer/members?page=3&pageSize=25'))).flush(
      page([], 30, 3),
    );
    (await vi.waitFor(() => controller.expectOne('/api/trainer/members?page=2&pageSize=25'))).flush(
      page(many(5, 25), 30, 2),
    );
    await settle();

    expect(router.url).toBe('/?page=2');
    expect(rows().length).toBe(5);
  });

  it('reports a failed load and offers a retry', async () => {
    await arrive();

    (await vi.waitFor(() => controller.expectOne(LIST))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();

    expect(html()).toContain('Nie udało się wczytać listy członków');

    root().querySelector<HTMLButtonElement>('.alert .link-button')!.click();
    (await vi.waitFor(() => controller.expectOne(LIST))).flush(page([ANNA]));
    await settle();

    expect(rows().length).toBe(1);
  });

  /** A refusal the API names takes the member-list table's words — the same table the admin's uses. */
  it('explains an invalid_search refusal with the member-list table', async () => {
    await arrive();

    (await vi.waitFor(() => controller.expectOne(LIST))).flush(
      { reason: 'invalid_search' },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(html()).toContain('Fraza wyszukiwania jest za długa');
  });
});
