import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Member, MemberPage } from '../../../core/admin/member-admin.models';
import { ToastService } from '../../../shared/toast/toast.service';
import { Members, SEARCH_DEBOUNCE_MS } from './members';

const ANNA: Member = {
  id: 'm1',
  userId: 'u1',
  email: 'anna@test.local',
  displayName: 'Anna Kowalska',
  membershipStatus: 'Active',
  accountStatus: 'Active',
  roles: ['User'],
  hasAccessCode: false,
  createdAt: '2026-09-01T08:00:00+00:00',
  passValidTo: null,
  passEntriesLeft: null,
};

const BARTEK: Member = {
  id: 'm2',
  userId: 'u2',
  email: 'bartek@test.local',
  displayName: 'Bartek Nowak',
  membershipStatus: 'Blocked',
  accountStatus: 'Blocked',
  roles: ['User'],
  hasAccessCode: false,
  createdAt: '2026-09-01T09:00:00+00:00',
  passValidTo: null,
  passEntriesLeft: null,
};

const CELINA: Member = {
  id: 'm3',
  userId: 'u3',
  email: 'celina@test.local',
  displayName: 'Celina Wiśniewska',
  membershipStatus: 'Active',
  accountStatus: 'Active',
  roles: ['User'],
  hasAccessCode: false,
  createdAt: '2026-09-01T10:00:00+00:00',
  passValidTo: null,
  passEntriesLeft: null,
};

/** An active member who already holds the Trainer role — the revoke direction. */
const DOROTA: Member = {
  id: 'm4',
  userId: 'u4',
  email: 'dorota@test.local',
  displayName: 'Dorota Lis',
  membershipStatus: 'Active',
  accountStatus: 'Active',
  roles: ['User', 'Trainer'],
  hasAccessCode: false,
  createdAt: '2026-09-01T11:00:00+00:00',
  passValidTo: null,
  passEntriesLeft: null,
};

/** The club's admin. S-04 stopped excluding admins from this list so FR-003's grant can reach them. */
const EWA: Member = {
  id: 'm5',
  userId: 'u5',
  email: 'ewa@test.local',
  displayName: 'Ewa Zając',
  membershipStatus: 'Active',
  accountStatus: 'Active',
  roles: ['Admin'],
  hasAccessCode: false,
  createdAt: '2026-09-01T12:00:00+00:00',
  passValidTo: null,
  passEntriesLeft: null,
};

/** A person the club recorded who has never registered — the case S-14 exists for. */
const FILIP: Member = {
  id: 'm6',
  userId: null,
  email: null,
  displayName: 'Filip Bez Konta',
  membershipStatus: 'Active',
  accountStatus: null,
  roles: [],
  hasAccessCode: false,
  createdAt: '2026-09-01T13:00:00+00:00',
  passValidTo: null,
  passEntriesLeft: null,
};

/** An accountless member who already has a code outstanding — the reveal and revoke directions. */
const GRAZYNA: Member = {
  id: 'm7',
  userId: null,
  email: null,
  displayName: 'Grażyna Bez Konta',
  membershipStatus: 'Active',
  accountStatus: null,
  roles: [],
  hasAccessCode: true,
  createdAt: '2026-09-01T14:00:00+00:00',
  passValidTo: null,
  passEntriesLeft: null,
};

/** The first page, unfiltered and unsearched — what the screen asks for on a plain visit. */
const LIST = '/api/admin/members?pageSize=10';

/** `rows` wrapped in the page envelope the API answers with (S-21). */
function page(items: Member[], total = items.length, pageNumber = 1): MemberPage {
  return { items, total, page: pageNumber, pageSize: 10 };
}

/** `count` distinct active members — a full page, for the pager tests. */
function many(count: number, offset = 0): Member[] {
  return Array.from({ length: count }, (_, index) => ({
    ...ANNA,
    id: `p${offset + index}`,
    displayName: `Członek ${offset + index}`,
  }));
}

describe('Members', () => {
  let fixture: ComponentFixture<Members>;
  let controller: HttpTestingController;
  let toasts: ToastService;
  let router: Router;

  /**
   * Arrives at `url` (the list's state lives in the query string since S-21), creates the component,
   * and returns its first list request unanswered.
   */
  async function arrive(url = '/') {
    TestBed.configureTestingModule({
      imports: [Members],
      // provideRouter, because the row menu's "Edytuj dane" entry is a real RouterLink (S-14) and
      // the screen reads and writes its state through the URL (S-21).
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    controller = TestBed.inject(HttpTestingController);
    toasts = TestBed.inject(ToastService);
    router = TestBed.inject(Router);

    await router.navigateByUrl(url);
    fixture = TestBed.createComponent(Members);
  }

  /** Creates the component and answers its initial first-page request with `rows`. */
  async function createWith(rows: Member[] | MemberPage, url = '/', request = LIST) {
    await arrive(url);

    (await vi.waitFor(() => controller.expectOne(request))).flush(
      Array.isArray(rows) ? page(rows) : rows,
    );
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function searchBox(): HTMLInputElement {
    return (fixture.nativeElement as HTMLElement).querySelector('input[type="search"]')!;
  }

  function type(value: string): void {
    const input = searchBox();
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function statusSelect(): HTMLSelectElement {
    return (fixture.nativeElement as HTMLElement).querySelector('#members-status')!;
  }

  function roleSelect(): HTMLSelectElement {
    return (fixture.nativeElement as HTMLElement).querySelector('#members-role')!;
  }

  /** Chooses the option showing `label` — what the admin picks — and fires the change. */
  function pick(select: HTMLSelectElement, label: string): void {
    const option = Array.from(select.options).find((o) => (o.textContent ?? '').trim() === label)!;
    select.value = option.value;
    select.dispatchEvent(new Event('change'));
  }

  /** The option the select shows as chosen. */
  function picked(select: HTMLSelectElement): string {
    return (select.selectedOptions[0]?.textContent ?? '').trim();
  }

  function pager(): HTMLElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector(
      'nav[aria-label="Strony listy członków"]',
    );
  }

  function pagerButton(label: string): HTMLButtonElement {
    return Array.from(pager()!.querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      (b.textContent ?? '').includes(label),
    )!;
  }

  afterEach(() => controller.verify());

  function html(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  /**
   * What this screen SAYS, as opposed to what it renders.
   *
   * Since S-19 every message from a row action goes to the toast rather than to a `.notice` banner
   * inside this component — the toast host is mounted once in the shell, so it is deliberately not
   * in this fixture. Reading the service is therefore reading the same thing the admin sees, and it
   * also lets the tone be asserted, which the banner never carried.
   */
  function toastText(): string {
    return toasts
      .toasts()
      .map((toast) => toast.message)
      .join(' ');
  }

  function toastTone(): string | undefined {
    return toasts.toasts().at(-1)?.tone;
  }

  /** A table's body rows since S-21 (UX-06) — the header row is not a member. */
  function rows(): HTMLElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr'));
  }

  function menuTrigger(row: HTMLElement): HTMLButtonElement {
    return row.querySelector<HTMLButtonElement>('[aria-haspopup="menu"]')!;
  }

  /** Opens the row's menu if it is closed, and returns its entries. */
  function openMenu(row: HTMLElement): HTMLButtonElement[] {
    const trigger = menuTrigger(row);
    if (trigger.getAttribute('aria-expanded') !== 'true') {
      trigger.click();
      fixture.detectChanges();
    }

    return Array.from(row.querySelectorAll<HTMLButtonElement>('[role="menuitem"]'));
  }

  function menuLabels(row: HTMLElement): string[] {
    return openMenu(row).map((item) => (item.textContent ?? '').trim());
  }

  /**
   * Row actions live behind a per-row menu since S-04, so opening it is part of reaching any of
   * them. Replaces the direct `buttonIn` lookup the pre-menu tests used.
   */
  function menuItemIn(row: HTMLElement, label: string): HTMLButtonElement {
    return openMenu(row).find((b) => (b.textContent ?? '').includes(label))!;
  }

  async function settle() {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('renders one row per member with a status badge', async () => {
    await createWith([ANNA, BARTEK]);

    expect(rows().length).toBe(2);
    expect(html()).toContain('Anna Kowalska');
    expect(html()).toContain('Aktywny');
    expect(html()).toContain('Zablokowany');
  });

  /**
   * A table, not a list of cards (S-21, UX-06): the columns are what an admin reads down. The same
   * DOM collapses to compact rows on a phone in CSS alone, so there is no second template to test.
   */
  it('lays the list out as a table with a header per column', async () => {
    await createWith([ANNA]);

    const table = (fixture.nativeElement as HTMLElement).querySelector('table')!;
    const headers = Array.from(table.querySelectorAll('thead th'));

    expect(table.querySelector('caption')!.textContent).toContain('Członkowie klubu');
    expect(headers.map((th) => (th.textContent ?? '').trim())).toEqual([
      'Członek',
      'Status',
      'Rola',
      'Karnet',
      'W klubie od',
      'Akcje',
    ]);
    expect(headers.every((th) => th.getAttribute('scope') === 'col')).toBe(true);

    // The name heads its row, so a screen reader moving across a row's cells hears whose they are.
    expect(rows()[0].querySelector('th[scope="row"]')!.textContent).toContain('Anna Kowalska');
    expect(rows()[0].textContent).toContain('anna@test.local');
  });

  /**
   * Moving into a table cell must not cost the row an action it offered as a list item. "Plan" is
   * S-22's (UX-07): a member's plan is reached through the member, next to Karnety.
   */
  it('keeps every action reachable from a row in the table', async () => {
    await createWith([FILIP]);

    expect(menuLabels(rows()[0])).toEqual([
      'Edytuj dane',
      'Karnety',
      'Plan',
      'Wygeneruj kod klubowicza',
      'Zablokuj',
    ]);

    const plan = Array.from(
      rows()[0].querySelectorAll<HTMLAnchorElement>('a[role="menuitem"]'),
    ).find((a) => a.textContent?.trim() === 'Plan');
    expect(plan!.getAttribute('href')).toBe(`/admin/members/${FILIP.id}/plan`);
  });

  /**
   * The S-14 row. "Bez konta" is a statement about their LOGIN, not a problem with their membership —
   * and approve must not be offered, because there is nothing to approve.
   */
  it('marks a member with no account and offers them no approval', async () => {
    await createWith([FILIP]);

    expect(html()).toContain('Filip Bez Konta');
    expect(html()).toContain('Bez konta');

    // A record kept at the desk may have no address; saying so beats an empty line.
    expect(html()).toContain('Brak adresu e-mail');

    const labels = menuLabels(rows()[0]);
    expect(labels).not.toContain('Zatwierdź');

    // Editing and blocking DO apply — they are the two things the club can do for someone whether or
    // not they ever sign in.
    expect(labels).toContain('Edytuj dane');
    expect(labels).toContain('Zablokuj');
  });

  // An empty list and a failed load must not look the same to the admin.
  it('renders an explicit empty state rather than a blank page', async () => {
    await createWith([]);

    expect(rows().length).toBe(0);
    expect(html()).toContain('Brak członków');
  });

  // --- search, paging and the URL (S-21) ------------------------------------

  /**
   * Search runs on the SERVER since S-21, after a pause. One request per pause, not per keystroke —
   * the difference between a search box and a load test — and with no `page`: a new phrase is a new
   * list, which starts at the top.
   */
  it('searches on the server once typing pauses, one request per pause', async () => {
    await createWith([ANNA, BARTEK], '/?page=2', '/api/admin/members?page=2&pageSize=10');

    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    try {
      type('k');
      vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS - 50);
      type('ko');
      vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS - 50);
      type('kow');
      vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS - 1);

      // Three keystrokes, none of them followed by a full pause: nothing has been asked yet.
      controller.expectNone((request) => request.url === '/api/admin/members');

      vi.advanceTimersByTime(1);
    } finally {
      vi.useRealTimers();
    }

    (
      await vi.waitFor(() => controller.expectOne('/api/admin/members?search=kow&pageSize=10'))
    ).flush(page([ANNA]));
    await settle();

    expect(rows().length).toBe(1);
    expect(html()).toContain('Anna Kowalska');

    // The phrase is in the address, and the page is gone from it.
    expect(router.url).toBe('/?q=kow');
  });

  /**
   * The pager used to navigate with the COMMITTED phrase, and the URL change then overwrote the box —
   * the admin's typing vanished. A pending phrase is a new list, so it wins over the page.
   */
  it('searches the pending phrase instead of discarding it when the pager is clicked', async () => {
    await createWith(page(many(10), 75));

    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    try {
      type('kow');
      vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS - 50);
      pagerButton('Następna').click();
    } finally {
      vi.useRealTimers();
    }

    (
      await vi.waitFor(() => controller.expectOne('/api/admin/members?search=kow&pageSize=10'))
    ).flush(page([ANNA]));
    await settle();

    expect(router.url).toBe('/?q=kow');
    expect(searchBox().value).toBe('kow');
  });

  /**
   * Typing replaces the history entry — a phrase typed a letter at a time must not leave a Back press
   * per pause — while a chip or a page is a place Back should return to, so those push.
   */
  it('replaces the history entry for a search, and pushes one for a filter or a page', async () => {
    await createWith(page(many(10), 75));
    const navigate = vi.spyOn(router, 'navigate');

    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    try {
      type('kow');
      vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    } finally {
      vi.useRealTimers();
    }
    (
      await vi.waitFor(() => controller.expectOne('/api/admin/members?search=kow&pageSize=10'))
    ).flush(page(many(10), 75));
    await settle();

    pagerButton('Następna').click();
    (
      await vi.waitFor(() =>
        controller.expectOne('/api/admin/members?search=kow&page=2&pageSize=10'),
      )
    ).flush(page(many(10, 10), 75, 2));
    await settle();

    pick(statusSelect(), 'Aktywni');
    (
      await vi.waitFor(() =>
        controller.expectOne('/api/admin/members?filter=Active&search=kow&pageSize=10'),
      )
    ).flush(page([ANNA]));
    await settle();

    expect(navigate.mock.calls.map(([, extras]) => extras?.replaceUrl)).toEqual([
      true,
      false,
      false,
    ]);
  });

  /**
   * Back and Forward change the URL under the screen; the box has to follow, or it would show one
   * phrase above the results of another.
   */
  it('puts the phrase from the URL into the search box on Back or Forward', async () => {
    await createWith([ANNA, BARTEK]);

    await router.navigateByUrl('/?q=nowak');
    (
      await vi.waitFor(() => controller.expectOne('/api/admin/members?search=nowak&pageSize=10'))
    ).flush(page([BARTEK]));
    await settle();

    expect(searchBox().value).toBe('nowak');

    await router.navigateByUrl('/');
    (await vi.waitFor(() => controller.expectOne(LIST))).flush(page([ANNA, BARTEK]));
    await settle();

    expect(searchBox().value).toBe('');
  });

  it('says the search found nobody, rather than that the view is empty', async () => {
    await createWith([], '/?q=zzz', '/api/admin/members?search=zzz&pageSize=10');

    expect(html()).toContain('Brak członków pasujących do wyszukiwania.');
    // The box shows the phrase the URL carries — a reload keeps what the admin typed.
    expect(searchBox().value).toBe('zzz');
  });

  /**
   * A URL an admin pasted or a bookmark from an older build may carry junk; the API would refuse it
   * with a 400. It is normalised away (replacing the history entry) instead — afterEach's verify()
   * is what proves no request ever carried it.
   */
  it('normalises junk in the URL without sending it to the API', async () => {
    await createWith([ANNA], '/?page=abc&filter=Foo&q=%20%20');

    expect(router.url).toBe('/');
    expect(rows().length).toBe(1);
  });

  it('restores page, phrase and filter from the URL', async () => {
    await createWith(
      page([ANNA], 30, 2),
      '/?q=kow&filter=Active&page=2',
      '/api/admin/members?filter=Active&search=kow&page=2&pageSize=10',
    );

    expect(searchBox().value).toBe('kow');
    expect(picked(statusSelect())).toBe('Aktywni');
    expect(html()).toContain('11–11 z 30');
  });

  /**
   * The karnet column says whether today is covered and until when. Staff hold no karnet (S-25),
   * so their cell stays empty rather than reading "Brak" as if it were something to fix.
   */
  it('shows the karnet covering today, and nothing for staff', async () => {
    await createWith([
      { ...ANNA, passValidTo: '2026-10-30', passEntriesLeft: 6 },
      { ...BARTEK, passValidTo: '2026-10-30', passEntriesLeft: 0 },
      CELINA,
      DOROTA,
    ]);

    const pass = (row: HTMLElement) =>
      (row.querySelector('.members-cell-pass')!.textContent ?? '').replace(/\s+/g, ' ').trim();

    expect(pass(rows()[0])).toContain('Ważny');
    expect(pass(rows()[0])).toMatch(/do 30 \S+ 2026 · 6 wejść/);
    expect(pass(rows()[1])).toContain('Wykorzystany');
    expect(pass(rows()[2])).toBe('Brak ważnego');
    expect(pass(rows()[3])).toBe('');
  });

  it('hides the pager when everything fits on one page', async () => {
    await createWith([ANNA, BARTEK]);

    expect(pager()).toBeNull();
  });

  it('pages forward and back through the server', async () => {
    await createWith(page(many(10), 15));

    expect(html()).toContain('1–10 z 15');
    expect(pagerButton('Poprzednia').disabled).toBe(true);

    pagerButton('Następna').click();
    (await vi.waitFor(() => controller.expectOne('/api/admin/members?page=2&pageSize=10'))).flush(
      page(many(5, 10), 15, 2),
    );
    await settle();

    expect(router.url).toBe('/?page=2');
    expect(html()).toContain('11–15 z 15');
    expect(pagerButton('Następna').disabled).toBe(true);

    pagerButton('Poprzednia').click();
    (await vi.waitFor(() => controller.expectOne(LIST))).flush(page(many(10), 15));
    await settle();

    expect(router.url).toBe('/');
  });

  /**
   * A reload keeps the table and the pager in the DOM. Swapping them for the spinner destroyed the
   * button the admin had pressed — focus fell to the body — and re-created the range's role="status"
   * already filled in, which a screen reader does not announce.
   */
  it('keeps the pager, its range and the focus in place while the next page loads', async () => {
    await createWith(page(many(10), 75));

    const nav = pager()!;
    const status = nav.querySelector('[role="status"]')!;
    const next = pagerButton('Następna');
    next.focus();
    next.click();

    const request = await vi.waitFor(() =>
      controller.expectOne('/api/admin/members?page=2&pageSize=10'),
    );
    await settle();

    // In flight: the old rows stay up, dimmed and inert, and the range still describes THEM.
    const table = (fixture.nativeElement as HTMLElement).querySelector('table')!;
    expect(table.getAttribute('aria-busy')).toBe('true');
    expect(rows().length).toBe(10);
    expect(menuTrigger(rows()[0]).disabled).toBe(true);
    expect(status.textContent).toContain('1–10 z 75');

    request.flush(page(many(10, 10), 75, 2));
    await settle();

    // The same elements, updated in place.
    expect(pager()).toBe(nav);
    expect(nav.querySelector('[role="status"]')).toBe(status);
    expect(status.textContent).toContain('11–20 z 75');
    expect(document.activeElement).toBe(next);
    expect(table.getAttribute('aria-busy')).toBe('false');
  });

  /**
   * The page ran out from under the URL — a block under Aktywni, or a bookmark from a bigger club.
   * "Brak członków" would be a lie, so the screen goes to the last page that exists.
   */
  it('lands on the last page when the requested one is past the end', async () => {
    await arrive('/?page=3');

    (await vi.waitFor(() => controller.expectOne('/api/admin/members?page=3&pageSize=10'))).flush(
      page([], 15, 3),
    );
    (await vi.waitFor(() => controller.expectOne('/api/admin/members?page=2&pageSize=10'))).flush(
      page(many(5, 10), 15, 2),
    );
    await settle();

    expect(router.url).toBe('/?page=2');
    expect(rows().length).toBe(5);
    expect(html()).not.toContain('Brak członków');
  });

  /** A list that emptied entirely goes to page 1 rather than showing "Brak" under a dead `?page=3`. */
  it('lands on the first page when the list emptied entirely', async () => {
    await arrive('/?filter=Blocked&page=3');

    (
      await vi.waitFor(() =>
        controller.expectOne('/api/admin/members?filter=Blocked&page=3&pageSize=10'),
      )
    ).flush(page([], 0, 3));
    (
      await vi.waitFor(() => controller.expectOne('/api/admin/members?filter=Blocked&pageSize=10'))
    ).flush(page([], 0));
    await settle();

    expect(router.url).toBe('/?filter=Blocked');
    expect(html()).toContain('Brak członków w tym widoku.');
  });

  /**
   * A chip navigates, keeping the phrase and dropping the page: page 2 of one filter says nothing
   * about page 2 of another.
   */
  it('refetches with the filter, keeping the phrase and dropping the page', async () => {
    await createWith(
      page([ANNA], 30, 2),
      '/?q=kow&page=2',
      '/api/admin/members?search=kow&page=2&pageSize=10',
    );

    pick(statusSelect(), 'Zablokowani');

    (
      await vi.waitFor(() =>
        controller.expectOne('/api/admin/members?filter=Blocked&search=kow&pageSize=10'),
      )
    ).flush(page([BARTEK]));
    await settle();

    expect(router.url).toBe('/?q=kow&filter=Blocked');
    expect(rows().length).toBe(1);
    expect(html()).toContain('Bartek Nowak');
  });

  /**
   * The role filter (persona, S-25) navigates exactly as the status one does: it keeps the phrase and
   * the other filter, and drops the page.
   */
  it('refetches with the role, keeping the phrase and the status', async () => {
    await createWith(
      page([ANNA], 30, 2),
      '/?q=kow&filter=Active&page=2',
      '/api/admin/members?filter=Active&search=kow&page=2&pageSize=10',
    );

    pick(roleSelect(), 'Trenerzy');

    (
      await vi.waitFor(() =>
        controller.expectOne(
          '/api/admin/members?filter=Active&role=Trainer&search=kow&pageSize=10',
        ),
      )
    ).flush(page([ANNA]));
    await settle();

    expect(router.url).toBe('/?q=kow&filter=Active&role=Trainer');
    expect(picked(roleSelect())).toBe('Trenerzy');
  });

  it('restores the role from the URL and drops an unknown one', async () => {
    await createWith([ANNA], '/?role=Admin', '/api/admin/members?role=Admin&pageSize=10');
    expect(picked(roleSelect())).toBe('Administratorzy');

    TestBed.resetTestingModule();
    await createWith([ANNA], '/?role=Owner');
    expect(router.url).toBe('/');
    expect(picked(roleSelect())).toBe('Wszystkie');
  });

  it('clears every filter at once', async () => {
    await createWith(
      [ANNA],
      '/?q=kow&filter=Blocked&role=Member',
      '/api/admin/members?filter=Blocked&role=Member&search=kow&pageSize=10',
    );

    const clear = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ).find((b) => (b.textContent ?? '').includes('Wyczyść filtry'))!;
    clear.click();

    (await vi.waitFor(() => controller.expectOne(LIST))).flush(page([ANNA, BARTEK]));
    await settle();

    expect(router.url).toBe('/');
    expect(searchBox().value).toBe('');
    expect(html()).toContain('2 osoby');
  });

  /**
   * Nothing cancels an in-flight request, so without a generation guard the last RESPONSE would
   * win rather than the last request — leaving the rows disagreeing with the highlighted chip.
   */
  it('discards a stale load response that resolves after a newer one', async () => {
    await createWith([ANNA, BARTEK]);

    // One at a time, so both navigations complete and both loads are really in flight together.
    pick(statusSelect(), 'Aktywni');
    const active = await vi.waitFor(() =>
      controller.expectOne('/api/admin/members?filter=Active&pageSize=10'),
    );
    pick(statusSelect(), 'Zablokowani');
    const blocked = await vi.waitFor(() =>
      controller.expectOne('/api/admin/members?filter=Blocked&pageSize=10'),
    );

    // The NEWER request answers first, the older one second — the out-of-order case.
    blocked.flush(page([BARTEK]));
    await settle();
    active.flush(page([ANNA]));
    await settle();

    // Blocked was the last filter chosen, so its rows must survive.
    expect(rows().length).toBe(1);
    expect(html()).toContain('Bartek Nowak');
    expect(html()).not.toContain('Anna Kowalska');
  });

  /**
   * A mutation resolving after the list moved on must not patch rows it never acted on — that
   * silently no-ops and makes a successful block look like it did nothing.
   */
  it('refetches instead of patching when the list reloaded mid-mutation', async () => {
    await createWith([ANNA]);

    menuItemIn(rows()[0], 'Zablokuj').click();
    const block = await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/block'));

    // The admin switches filter before the block comes back.
    pick(statusSelect(), 'Zablokowani');
    (
      await vi.waitFor(() => controller.expectOne('/api/admin/members?filter=Blocked&pageSize=10'))
    ).flush(page([]));
    await settle();

    block.flush(null);
    await settle();

    // A refetch, not a silent no-op against a list this mutation never saw.
    (
      await vi.waitFor(() => controller.expectOne('/api/admin/members?filter=Blocked&pageSize=10'))
    ).flush(page([{ ...ANNA, membershipStatus: 'Blocked', accountStatus: 'Blocked' }]));
    await settle();

    expect(rows().length).toBe(1);
    expect(html()).toContain('Zablokowany');
  });

  /**
   * The core difference from the approvals screen: a blocked member still belongs on this list.
   * Removing the row would tell the admin the member vanished.
   */
  it('updates the row in place on block, without removing it', async () => {
    await createWith([ANNA]);

    menuItemIn(rows()[0], 'Zablokuj').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/block'))).flush(null);
    await settle();

    expect(rows().length).toBe(1);
    expect(html()).toContain('Anna Kowalska');
    expect(html()).toContain('Zablokowany');
    expect(html()).not.toContain('Aktywny');
  });

  it('updates the row in place on unblock', async () => {
    await createWith([BARTEK]);

    menuItemIn(rows()[0], 'Odblokuj').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m2/unblock'))).flush(null);
    await settle();

    expect(rows().length).toBe(1);
    expect(html()).toContain('Aktywny');
  });

  /**
   * APPROVE IS GONE (S-16, MP-03), asserted as an absence. Nothing produces a pending account any
   * more, so a Zatwierdź item on any row would be an action with no outcome.
   */
  it('offers no approve action on any row', async () => {
    await createWith([CELINA]);

    expect(html()).not.toContain('Zatwierdź');
  });

  /**
   * 409 means our view is stale or the action was refused. Guessing what the row became is how a
   * screen ends up lying about state it never saw — so it refetches.
   */
  it('refetches and explains when block is refused for an admin', async () => {
    await createWith([ANNA]);

    menuItemIn(rows()[0], 'Zablokuj').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/block'))).flush(
      { reason: 'is_admin' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    (await vi.waitFor(() => controller.expectOne(LIST))).flush(page([ANNA]));
    await settle();

    expect(toastText()).toContain('zarządza klubem');
    // A rule the admin has to do something about, so it is an error and does not time out.
    expect(toastTone()).toBe('error');
    expect(html()).toContain('Aktywny');
  });

  it('refetches on a lost-race conflict', async () => {
    await createWith([ANNA]);

    menuItemIn(rows()[0], 'Zablokuj').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/block'))).flush(
      { reason: 'conflict' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    (await vi.waitFor(() => controller.expectOne(LIST))).flush(
      page([{ ...ANNA, membershipStatus: 'Blocked', accountStatus: 'Blocked' }]),
    );
    await settle();

    expect(toastText()).toContain('nieaktualna');
    // NOT an error: nothing was wrong except the timing, and the list has just been reloaded. This
    // is the distinction the single `.notice` banner could not draw — see AGENTS.md, outlet 3.
    expect(toastTone()).toBe('info');
    expect(html()).toContain('Zablokowany');
  });

  /**
   * The failure that matters: an admin who believes someone was blocked when they were not. The row
   * must keep its CURRENT status, not the intended one.
   */
  it('leaves the status unchanged and surfaces the error when block fails', async () => {
    await createWith([ANNA]);

    menuItemIn(rows()[0], 'Zablokuj').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/block'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();

    expect(rows().length).toBe(1);
    expect(html()).toContain('Aktywny');
    expect(html()).not.toContain('Zablokowany');
    expect(html()).toContain('Nie udało się');

    // Still actionable — a failed block must be retryable. Busy-state now lives on the menu
    // trigger rather than on the individual action, so that is what must be re-enabled.
    expect(menuTrigger(rows()[0]).disabled).toBe(false);
    expect(menuItemIn(rows()[0], 'Zablokuj')).toBeTruthy();
  });

  /** The refetch after a refusal reloads the page the admin is ON, not page 1. */
  it('reloads the current page when a block is refused', async () => {
    await createWith(page([ANNA], 30, 2), '/?page=2', '/api/admin/members?page=2&pageSize=10');

    menuItemIn(rows()[0], 'Zablokuj').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/block'))).flush(
      { reason: 'conflict' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members?page=2&pageSize=10'))).flush(
      page([ANNA], 30, 2),
    );
    await settle();

    expect(router.url).toBe('/?page=2');
  });

  it('reports a failed load and offers a retry', async () => {
    await arrive();

    (await vi.waitFor(() => controller.expectOne(LIST))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();

    expect(html()).toContain('Nie udało się wczytać');

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.link-button')!
      .click();

    (await vi.waitFor(() => controller.expectOne(LIST))).flush(page([ANNA]));
    await settle();

    expect(rows().length).toBe(1);
  });

  // --- roles and the row menu (S-04) ----------------------------------------

  /** `User` gets no badge: every member holds it, so a badge on every row distinguishes nothing. */
  it('badges notable roles and stays silent about the member role', async () => {
    await createWith([ANNA, DOROTA, EWA]);

    expect(html()).toContain('Trener');
    expect(html()).toContain('Administrator');

    const annaBadges = Array.from(rows()[0].querySelectorAll('.badge-role'));
    expect(annaBadges.length).toBe(0);
  });

  it('offers the grant direction on an active member without the role', async () => {
    await createWith([ANNA]);

    expect(menuLabels(rows()[0])).toContain('Nadaj rolę Trenera');
  });

  it('offers the revoke direction on a member who already holds the role', async () => {
    await createWith([DOROTA]);

    expect(menuLabels(rows()[0])).toContain('Odbierz rolę Trenera');
  });

  /**
   * Mirrors the API's not_active guard — a button whose only outcome is a 409 is not an action.
   *
   * ONE ROW SINCE S-16, not two. This used to cover a blocked account and a pending one; nothing
   * produces a pending account any more, so blocked is the whole of what the guard can still refuse.
   * See MemberAdminEndpoints' TrainerRoleFailure for what that guard does and no longer guarantees.
   */
  it('hides the role action on a blocked row', async () => {
    await createWith([BARTEK]);

    expect(menuLabels(rows()[0]).join(' ')).not.toContain('Trenera');
  });

  /**
   * Admins appear on this list since S-04, but the API refuses to block them (is_admin), so
   * offering it would only produce a 409. The role action must still be there — that is the whole
   * reason admins became visible (FR-003).
   */
  /**
   * S-25: trainers and admins hold no karnet and no plan, so their rows stop offering either — the
   * API refuses both with member_is_staff. A plain member's row keeps both.
   */
  it('offers Karnety and Plan on a member row but not on a staff row', async () => {
    await createWith([ANNA, DOROTA, EWA]);

    const links = (row: HTMLElement) =>
      openMenu(row)
        .map((item) => item.getAttribute('href') ?? '')
        .filter((href) => href.endsWith('/passes') || href.endsWith('/plan'));

    expect(links(rows()[0])).toEqual([
      `/admin/members/${ANNA.id}/passes`,
      `/admin/members/${ANNA.id}/plan`,
    ]);
    expect(links(rows()[1])).toEqual([]);
    expect(links(rows()[2])).toEqual([]);
  });

  it('offers the role action but not block on an admin row', async () => {
    await createWith([EWA]);

    const labels = menuLabels(rows()[0]);
    expect(labels).toContain('Nadaj rolę Trenera');
    expect(labels.join(' ')).not.toContain('Zablokuj');
    expect(labels.join(' ')).not.toContain('Odblokuj');
  });

  it('patches the row in place when the role is granted', async () => {
    await createWith([ANNA]);

    menuItemIn(rows()[0], 'Nadaj rolę Trenera').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/roles/trainer'))).flush(
      null,
    );
    await settle();

    expect(rows().length).toBe(1);
    expect(html()).toContain('Trener');
    expect(menuLabels(rows()[0])).toContain('Odbierz rolę Trenera');
  });

  it('patches the row in place when the role is revoked', async () => {
    await createWith([DOROTA]);

    menuItemIn(rows()[0], 'Odbierz rolę Trenera').click();

    const request = await vi.waitFor(() =>
      controller.expectOne('/api/admin/members/m4/roles/trainer'),
    );
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
    await settle();

    expect(rows().length).toBe(1);
    expect(rows()[0].querySelectorAll('.badge-role').length).toBe(0);
  });

  it('refetches and explains when the role change is refused as not_active', async () => {
    await createWith([ANNA]);

    menuItemIn(rows()[0], 'Nadaj rolę Trenera').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/roles/trainer'))).flush(
      { reason: 'not_active' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    (await vi.waitFor(() => controller.expectOne(LIST))).flush(
      page([{ ...ANNA, membershipStatus: 'Blocked', accountStatus: 'Blocked' }]),
    );
    await settle();

    // "nie jest aktywna", not "aktywny": the sentence no longer interpolates the member's display
    // name (S-19 — a table keyed by a reason returns a sentence, not a template), so it is phrased
    // in the third person the way booking-failure.ts already was.
    expect(toastText()).toContain('nie jest aktywna');
    expect(toastTone()).toBe('error');
    expect(html()).toContain('Zablokowany');
  });

  it('closes the menu and returns focus to its trigger on Escape', async () => {
    await createWith([ANNA]);

    const trigger = menuTrigger(rows()[0]);
    openMenu(rows()[0]);
    expect(trigger.getAttribute('aria-expanded')).toBe('true');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();

    expect(menuTrigger(rows()[0]).getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(trigger);
  });

  it('closes the menu on a click outside it', async () => {
    await createWith([ANNA]);

    openMenu(rows()[0]);
    expect(menuTrigger(rows()[0]).getAttribute('aria-expanded')).toBe('true');

    document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();

    expect(menuTrigger(rows()[0]).getAttribute('aria-expanded')).toBe('false');
  });

  it('keeps at most one menu open', async () => {
    await createWith([ANNA, DOROTA]);

    openMenu(rows()[0]);
    openMenu(rows()[1]);

    expect(menuTrigger(rows()[0]).getAttribute('aria-expanded')).toBe('false');
    expect(menuTrigger(rows()[1]).getAttribute('aria-expanded')).toBe('true');
  });

  // --- member code (S-14) ---------------------------------------------------

  /**
   * The code's only power is "attach the account being created to this record", so a person who
   * already logs in has nothing to claim — and the API answers has_account. Offering it there would
   * put an action on screen whose only outcome is a 409.
   */
  it('offers the code only to a member with no account', async () => {
    await createWith([ANNA, FILIP]);

    expect(menuLabels(rows()[0])).not.toContain('Wygeneruj kod klubowicza');
    expect(menuLabels(rows()[1])).toContain('Wygeneruj kod klubowicza');
  });

  /** Nothing to reveal or revoke until one exists, so the row offers exactly one code action. */
  it('offers only generation while no code is outstanding', async () => {
    await createWith([FILIP]);

    const labels = menuLabels(rows()[0]);
    expect(labels).toContain('Wygeneruj kod klubowicza');
    expect(labels).not.toContain('Pokaż kod klubowicza');
    expect(labels).not.toContain('Unieważnij kod');
  });

  it('shows the issued code and turns the row into a reveal-and-revoke one', async () => {
    await createWith([FILIP]);

    menuItemIn(rows()[0], 'Wygeneruj kod klubowicza').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m6/access-code'))).flush({
      code: 'ABCD-2345',
      expiresAt: '2026-09-22T14:00:00+00:00',
    });
    await settle();

    // The formatted form exactly as the API sent it — the dash is presentation the API owns.
    expect(html()).toContain('ABCD-2345');
    expect(html()).toContain('Ważny do');

    const labels = menuLabels(rows()[0]);
    expect(labels).toContain('Pokaż kod klubowicza');
    expect(labels).toContain('Wygeneruj nowy kod');
    expect(labels).toContain('Unieważnij kod');
  });

  /**
   * An expired code is reported by the API as none at all, because reading out a code registration
   * will refuse is worse than reading out nothing. The row corrects itself rather than keeping the
   * stale flag.
   */
  it('reports an empty reveal as no code and clears the row flag', async () => {
    await createWith([GRAZYNA]);

    menuItemIn(rows()[0], 'Pokaż kod klubowicza').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m7/access-code'))).flush(
      null,
      {
        status: 204,
        statusText: 'No Content',
      },
    );
    await settle();

    expect(html()).toContain('Brak ważnego kodu');
    expect(menuLabels(rows()[0])).not.toContain('Unieważnij kod');
  });

  it('clears the code from the row when it is revoked', async () => {
    await createWith([GRAZYNA]);

    menuItemIn(rows()[0], 'Unieważnij kod').click();

    const request = await vi.waitFor(() =>
      controller.expectOne('/api/admin/members/m7/access-code'),
    );
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    expect(toastText()).toContain('unieważniony');
    expect(toastTone()).toBe('success');
    expect(menuLabels(rows()[0])).not.toContain('Pokaż kod klubowicza');
  });

  /**
   * S-17, IR-06. The admin's ordinary delivery is now a link they paste into a message, so the panel
   * offers the whole URL — with `invitationCode`, the name the register screen reads, and NOT
   * `memberCode`, which is the API field the two deliberately disagree on.
   *
   * <p>
   * The BARE CODE stays copyable beside it: it exists to be read down the phone, which is the entire
   * reason its alphabet drops the characters people confuse when transcribing.
   * </p>
   */
  it('copies the invitation link carrying the code, and keeps the bare code copyable', async () => {
    // Only `clipboard` is replaced, and through defineProperty rather than vi.stubGlobal: swapping
    // the whole navigator loses the prototype getters Angular's forms read (userAgent), which fails
    // every subsequent render rather than this test.
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText },
      configurable: true,
    });

    await createWith([FILIP]);

    menuItemIn(rows()[0], 'Wygeneruj kod klubowicza').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m6/access-code'))).flush({
      code: 'ABCD-2345',
      expiresAt: '2026-09-22T14:00:00+00:00',
    });
    await settle();

    const buttons = Array.from(
      rows()[0].querySelectorAll<HTMLButtonElement>('.member-code-actions .link-button'),
    );

    buttons.find((b) => b.textContent?.includes('Kopiuj link'))!.click();
    await settle();

    const [link] = writeText.mock.calls[0] as [string];
    expect(link).toContain('/register?invitationCode=');
    expect(link).toContain('ABCD-2345');

    // The origin is the document's own, so the link is right on localhost and in production alike
    // without a configured base URL to keep in step.
    expect(link.startsWith(document.location.origin)).toBe(true);

    // And the code alone is still one click away, for the admin reading it out at the desk.
    buttons.find((b) => b.textContent?.includes('Kopiuj kod'))!.click();
    await settle();

    expect(writeText).toHaveBeenLastCalledWith('ABCD-2345');

    Reflect.deleteProperty(navigator, 'clipboard');
  });

  /**
   * The member registered between the list loading and the admin pressing the button. The list is
   * stale, so it is refetched rather than patched from a guess — the rule every action here follows.
   */
  it('refetches and explains when the member turns out to have an account', async () => {
    await createWith([FILIP]);

    menuItemIn(rows()[0], 'Wygeneruj kod klubowicza').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m6/access-code'))).flush(
      { reason: 'has_account' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    (await vi.waitFor(() => controller.expectOne(LIST))).flush(page([ANNA]));
    await settle();

    expect(toastText()).toContain('ma już konto');
    expect(toastTone()).toBe('error');
    expect(html()).not.toContain('ABCD-2345');
  });
});
