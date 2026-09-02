import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MemberAdminService } from './member-admin.service';
import { Member, PendingMember } from './member-admin.models';

const MEMBER: PendingMember = {
  id: 'm1',
  email: 'nowy@test.local',
  displayName: 'Nowy Członek',
  createdAt: '2026-09-01T08:00:00+00:00',
};

const FULL_MEMBER: Member = {
  ...MEMBER,
  status: 'Active',
  roles: ['User'],
};

describe('MemberAdminService', () => {
  let service: MemberAdminService;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(MemberAdminService);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('reads the pending queue from /api/admin/members/pending', async () => {
    const pending = service.getPending();

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/members/pending'));
    expect(request.request.method).toBe('GET');
    request.flush([MEMBER]);

    await expect(pending).resolves.toEqual([MEMBER]);
  });

  it('posts approve to the member-scoped path', async () => {
    const approved = service.approve('m1');

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/approve'));
    expect(request.request.method).toBe('POST');
    request.flush(null);

    await expect(approved).resolves.toBeUndefined();
  });

  // Identity's default key is a GUID, but the type is a string and the path is built by hand — an
  // unencoded id would silently produce a request to the wrong URL.
  it('encodes the id into the path', async () => {
    const approved = service.approve('a b/c');

    const request = await vi.waitFor(() =>
      controller.expectOne('/api/admin/members/a%20b%2Fc/approve'),
    );
    request.flush(null);

    await approved;
  });

  it('reads the full member list from /api/admin/members', async () => {
    const members = service.getMembers();

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/members'));
    expect(request.request.method).toBe('GET');
    request.flush([FULL_MEMBER]);

    await expect(members).resolves.toEqual([FULL_MEMBER]);
  });

  it('sends the status as a query parameter when filtering', async () => {
    const members = service.getMembers('Blocked');

    const request = await vi.waitFor(() =>
      controller.expectOne('/api/admin/members?status=Blocked'),
    );
    request.flush([]);

    await members;
  });

  /**
   * The endpoint binds status as a nullable enum and 400s on an unparseable value, so `?status=`
   * would be a broken request rather than "no filter". The parameter has to be absent, not empty.
   */
  it('omits the status parameter entirely when unfiltered', async () => {
    const members = service.getMembers();

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/members'));
    expect(request.request.params.has('status')).toBe(false);
    request.flush([]);

    await members;
  });

  it('posts block and unblock to the member-scoped paths', async () => {
    const blocked = service.block('m1');
    const blockRequest = await vi.waitFor(() =>
      controller.expectOne('/api/admin/members/m1/block'),
    );
    expect(blockRequest.request.method).toBe('POST');
    blockRequest.flush(null);
    await expect(blocked).resolves.toBeUndefined();

    const unblocked = service.unblock('m1');
    const unblockRequest = await vi.waitFor(() =>
      controller.expectOne('/api/admin/members/m1/unblock'),
    );
    expect(unblockRequest.request.method).toBe('POST');
    unblockRequest.flush(null);
    await expect(unblocked).resolves.toBeUndefined();
  });

  // Nothing here catches: the screen has to know an approve failed so it can keep the row.
  it('rejects rather than swallowing a failed approve', async () => {
    const approved = service.approve('m1');

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/approve'))).flush(
      { reason: 'not_pending' },
      { status: 409, statusText: 'Conflict' },
    );

    await expect(approved).rejects.toBeDefined();
  });

  /** Grant and revoke share a path and differ only by verb, so both are asserted together. */
  it('grants with POST and revokes with DELETE on the same role path', async () => {
    const granted = service.grantTrainer('m1');

    const grantRequest = await vi.waitFor(() =>
      controller.expectOne('/api/admin/members/m1/roles/trainer'),
    );
    expect(grantRequest.request.method).toBe('POST');
    grantRequest.flush(null);
    await expect(granted).resolves.toBeUndefined();

    const revoked = service.revokeTrainer('m1');

    const revokeRequest = await vi.waitFor(() =>
      controller.expectOne('/api/admin/members/m1/roles/trainer'),
    );
    expect(revokeRequest.request.method).toBe('DELETE');
    revokeRequest.flush(null);
    await expect(revoked).resolves.toBeUndefined();
  });

  it('rejects rather than swallowing a refused role change', async () => {
    const granted = service.grantTrainer('m1');

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/roles/trainer'))).flush(
      { reason: 'not_active' },
      { status: 409, statusText: 'Conflict' },
    );

    await expect(granted).rejects.toBeDefined();
  });
});
