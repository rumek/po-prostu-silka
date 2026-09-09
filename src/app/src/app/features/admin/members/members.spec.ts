import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Member } from '../../../core/admin/member-admin.models';
import { Members } from './members';

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
};

describe('Members', () => {
  let fixture: ComponentFixture<Members>;
  let controller: HttpTestingController;

  /** Creates the component and answers its initial unfiltered request with `rows`. */
  async function createWith(rows: Member[]) {
    TestBed.configureTestingModule({
      imports: [Members],
      // provideRouter, because the row menu's "Edytuj dane" entry is a real RouterLink (S-14) and
      // the directive needs an ActivatedRoute to resolve its href.
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
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

  // The filter maps onto the API's own query, so unlike search it DOES refetch.
  it('refetches with a filter parameter when the filter changes', async () => {
    await createWith([ANNA, BARTEK]);

    const chip = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('.chip'),
    ).find((b) => (b.textContent ?? '').includes('Zablokowani'))!;
    chip.click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members?filter=Blocked'))).flush([
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

    const active = await vi.waitFor(() => controller.expectOne('/api/admin/members?filter=Active'));
    const blocked = await vi.waitFor(() =>
      controller.expectOne('/api/admin/members?filter=Blocked'),
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
    (await vi.waitFor(() => controller.expectOne('/api/admin/members?filter=Blocked'))).flush([]);
    await settle();

    block.flush(null);
    await settle();

    // A refetch, not a silent no-op against a list this mutation never saw.
    (await vi.waitFor(() => controller.expectOne('/api/admin/members?filter=Blocked'))).flush([
      { ...ANNA, membershipStatus: 'Blocked', accountStatus: 'Blocked' },
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
      { ...ANNA, membershipStatus: 'Blocked', accountStatus: 'Blocked' },
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
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
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
      { ...ANNA, membershipStatus: 'Blocked', accountStatus: 'Blocked' },
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

    expect(html()).toContain('unieważniony');
    expect(menuLabels(rows()[0])).not.toContain('Pokaż kod klubowicza');
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

    (await vi.waitFor(() => controller.expectOne('/api/admin/members'))).flush([ANNA]);
    await settle();

    expect(html()).toContain('ma już konto');
    expect(html()).not.toContain('ABCD-2345');
  });
});
