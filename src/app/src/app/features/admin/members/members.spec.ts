import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Member } from '../../../core/admin/member-admin.models';
import { Members } from './members';

const ANNA: Member = {
  id: 'm1',
  email: 'anna@test.local',
  displayName: 'Anna Kowalska',
  status: 'Active',
  roles: ['User'],
  createdAt: '2026-09-01T08:00:00+00:00',
};

const BARTEK: Member = {
  id: 'm2',
  email: 'bartek@test.local',
  displayName: 'Bartek Nowak',
  status: 'Blocked',
  roles: ['User'],
  createdAt: '2026-09-01T09:00:00+00:00',
};

const CELINA: Member = {
  id: 'm3',
  email: 'celina@test.local',
  displayName: 'Celina Wiśniewska',
  status: 'Pending',
  roles: ['User'],
  createdAt: '2026-09-01T10:00:00+00:00',
};

/** An active member who already holds the Trainer role — the revoke direction. */
const DOROTA: Member = {
  id: 'm4',
  email: 'dorota@test.local',
  displayName: 'Dorota Lis',
  status: 'Active',
  roles: ['User', 'Trainer'],
  createdAt: '2026-09-01T11:00:00+00:00',
};

/** The club's admin. S-04 stopped excluding admins from this list so FR-003's grant can reach them. */
const EWA: Member = {
  id: 'm5',
  email: 'ewa@test.local',
  displayName: 'Ewa Zając',
  status: 'Active',
  roles: ['Admin'],
  createdAt: '2026-09-01T12:00:00+00:00',
};

describe('Members', () => {
  let fixture: ComponentFixture<Members>;
  let controller: HttpTestingController;

  /** Creates the component and answers its initial unfiltered request with `rows`. */
  async function createWith(rows: Member[]) {
    TestBed.configureTestingModule({
      imports: [Members],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Members);

    (await vi.waitFor(() => controller.expectOne('/api/admin/members'))).flush(rows);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => controller.verify());

  function html(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function rows(): HTMLElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.members-row'));
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

  // The admin exclusion lives in the API; the screen must not paper over an empty result.
  it('renders an explicit empty state rather than a blank page', async () => {
    await createWith([]);

    expect(rows().length).toBe(0);
    expect(html()).toContain('Brak członków');
  });

  /**
   * Search is client-side by design — a request per keystroke would need debouncing to buy nothing
   * on a list this size. afterEach's controller.verify() is what proves no request was issued.
   */
  it('narrows on search without refetching', async () => {
    await createWith([ANNA, BARTEK]);

    const input = (fixture.nativeElement as HTMLElement).querySelector('input')!;
    input.value = 'bartek';
    input.dispatchEvent(new Event('input'));
    await settle();

    expect(rows().length).toBe(1);
    expect(html()).toContain('Bartek Nowak');
    expect(html()).not.toContain('Anna Kowalska');
  });

  it('matches search against the email as well as the display name', async () => {
    await createWith([ANNA, BARTEK]);

    const input = (fixture.nativeElement as HTMLElement).querySelector('input')!;
    input.value = 'ANNA@TEST';
    input.dispatchEvent(new Event('input'));
    await settle();

    expect(rows().length).toBe(1);
    expect(html()).toContain('Anna Kowalska');
  });

  // The status filter maps onto the API's indexed query, so unlike search it DOES refetch.
  it('refetches with a status parameter when the filter changes', async () => {
    await createWith([ANNA, BARTEK]);

    const chip = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.chip'),
    ).find((b) => (b.textContent ?? '').includes('Zablokowani'))!;
    chip.click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members?status=Blocked'))).flush([
      BARTEK,
    ]);
    await settle();

    expect(rows().length).toBe(1);
    expect(html()).toContain('Bartek Nowak');
  });

  /**
   * Nothing cancels an in-flight request, so without a generation guard the last RESPONSE would
   * win rather than the last request — leaving the rows disagreeing with the highlighted chip.
   */
  it('discards a stale load response that resolves after a newer one', async () => {
    await createWith([ANNA, BARTEK]);

    const chips = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.chip'),
    );
    chips.find((b) => (b.textContent ?? '').includes('Aktywni'))!.click();
    chips.find((b) => (b.textContent ?? '').includes('Zablokowani'))!.click();

    const active = await vi.waitFor(() => controller.expectOne('/api/admin/members?status=Active'));
    const blocked = await vi.waitFor(() =>
      controller.expectOne('/api/admin/members?status=Blocked'),
    );

    // The NEWER request answers first, the older one second — the out-of-order case.
    blocked.flush([BARTEK]);
    await settle();
    active.flush([ANNA]);
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
    Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.chip'))
      .find((b) => (b.textContent ?? '').includes('Zablokowani'))!
      .click();
    (await vi.waitFor(() => controller.expectOne('/api/admin/members?status=Blocked'))).flush([]);
    await settle();

    block.flush(null);
    await settle();

    // A refetch, not a silent no-op against a list this mutation never saw.
    (await vi.waitFor(() => controller.expectOne('/api/admin/members?status=Blocked'))).flush([
      { ...ANNA, status: 'Blocked' },
    ]);
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

  it('offers approve on a pending row', async () => {
    await createWith([CELINA]);

    menuItemIn(rows()[0], 'Zatwierdź').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m3/approve'))).flush(null);
    await settle();

    expect(html()).toContain('Aktywny');
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

    (await vi.waitFor(() => controller.expectOne('/api/admin/members'))).flush([ANNA]);
    await settle();

    expect(html()).toContain('zarządza klubem');
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

    (await vi.waitFor(() => controller.expectOne('/api/admin/members'))).flush([
      { ...ANNA, status: 'Blocked' },
    ]);
    await settle();

    expect(html()).toContain('nieaktualna');
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

  it('reports a failed load and offers a retry', async () => {
    TestBed.configureTestingModule({
      imports: [Members],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Members);

    (await vi.waitFor(() => controller.expectOne('/api/admin/members'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();

    expect(html()).toContain('Nie udało się wczytać');

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.link-button')!
      .click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members'))).flush([ANNA]);
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

  /** Mirrors the API's not_active guard — a button whose only outcome is a 409 is not an action. */
  it('hides the role action on non-active rows', async () => {
    await createWith([BARTEK, CELINA]);

    expect(menuLabels(rows()[0]).join(' ')).not.toContain('Trenera');
    expect(menuLabels(rows()[1]).join(' ')).not.toContain('Trenera');
  });

  /**
   * Admins appear on this list since S-04, but the API refuses to block them (is_admin), so
   * offering it would only produce a 409. The role action must still be there — that is the whole
   * reason admins became visible (FR-003).
   */
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

    (await vi.waitFor(() => controller.expectOne('/api/admin/members'))).flush([
      { ...ANNA, status: 'Blocked' },
    ]);
    await settle();

    expect(html()).toContain('nie jest aktywny');
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
});
