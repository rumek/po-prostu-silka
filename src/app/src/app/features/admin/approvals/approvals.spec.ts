import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PendingMember } from '../../../core/admin/member-admin.models';
import { Approvals } from './approvals';

const ANNA: PendingMember = {
  id: 'm1',
  email: 'anna@test.local',
  displayName: 'Anna Kowalska',
  createdAt: '2026-09-01T08:00:00+00:00',
};

const BARTEK: PendingMember = {
  id: 'm2',
  email: 'bartek@test.local',
  displayName: 'Bartek Nowak',
  createdAt: '2026-09-01T09:00:00+00:00',
};

describe('Approvals', () => {
  let fixture: ComponentFixture<Approvals>;
  let controller: HttpTestingController;

  /** Creates the component and answers its initial pending-queue request with `rows`. */
  async function createWith(rows: PendingMember[]) {
    TestBed.configureTestingModule({
      imports: [Approvals],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Approvals);

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/pending'))).flush(rows);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => controller.verify());

  function html(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function rows(): HTMLElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.approvals-row'));
  }

  function approveButtonIn(row: HTMLElement): HTMLButtonElement {
    return row.querySelector('button')!;
  }

  it('renders one row per pending member', async () => {
    await createWith([ANNA, BARTEK]);

    expect(rows().length).toBe(2);
    expect(html()).toContain('Anna Kowalska');
    expect(html()).toContain('anna@test.local');
  });

  // "Nothing to do" and "it failed to load" must not look the same to the admin.
  it('renders an explicit empty state rather than a blank page', async () => {
    await createWith([]);

    expect(rows().length).toBe(0);
    expect(html()).toContain('Brak zgłoszeń');
  });

  it('removes the row on a successful approve, without refetching the list', async () => {
    await createWith([ANNA, BARTEK]);

    approveButtonIn(rows()[0]).click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/approve'))).flush(null);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(rows().length).toBe(1);
    expect(html()).not.toContain('Anna Kowalska');

    // afterEach's controller.verify() is what proves no refetch was issued.
  });

  /**
   * The failure that matters on this screen: an admin who believes someone was approved when they
   * were not. Nothing else in the product would ever correct that belief.
   */
  it('keeps the row and surfaces the error when approve fails', async () => {
    await createWith([ANNA]);

    approveButtonIn(rows()[0]).click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/approve'))).flush(
      { reason: 'not_pending' },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(rows().length).toBe(1);
    expect(html()).toContain('Nie udało się zatwierdzić');

    // Still actionable — a failed approve must be retryable.
    expect(approveButtonIn(rows()[0]).disabled).toBe(false);
  });

  it('reports a failed load and offers a retry', async () => {
    TestBed.configureTestingModule({
      imports: [Approvals],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Approvals);

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/pending'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(html()).toContain('Nie udało się wczytać');

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.link-button')!
      .click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/pending'))).flush([ANNA]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(rows().length).toBe(1);
  });
});
