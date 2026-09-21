import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MemberAdminService } from './member-admin.service';
import { Member } from './member-admin.models';

const FULL_MEMBER: Member = {
  id: 'm1',
  userId: 'u1',
  email: 'nowy@test.local',
  displayName: 'Nowy Członek',
  createdAt: '2026-09-01T08:00:00+00:00',
  membershipStatus: 'Active',
  accountStatus: 'Active',
  roles: ['User'],
  hasAccessCode: false,
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

  // Identity's default key is a GUID, but the type is a string and the path is built by hand — an
  // unencoded id would silently produce a request to the wrong URL.
  it('encodes the id into the path', async () => {
    const approved = service.block('a b/c');

    const request = await vi.waitFor(() =>
      controller.expectOne('/api/admin/members/a%20b%2Fc/block'),
    );
    request.flush(null);

    await approved;
  });

  it('reads a page of the member list from /api/admin/members', async () => {
    const members = service.getMembers();

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/members'));
    expect(request.request.method).toBe('GET');
    const page = { items: [FULL_MEMBER], total: 1, page: 1, pageSize: 25 };
    request.flush(page);

    await expect(members).resolves.toEqual(page);
  });

  it('sends the filter, search, page and page size as query parameters', async () => {
    const members = service.getMembers({
      filter: 'Blocked',
      search: '  kowal ',
      page: 3,
      pageSize: 25,
    });

    // The phrase goes out TRIMMED: a search for " kowal" and one for "kowal" are the same question.
    const request = await vi.waitFor(() =>
      controller.expectOne('/api/admin/members?filter=Blocked&search=kowal&page=3&pageSize=25'),
    );
    request.flush({ items: [], total: 0, page: 3, pageSize: 25 });

    await members;
  });

  /**
   * The endpoint binds the filter as a nullable enum and 400s on an unparseable value, so `?filter=`
   * would be a broken request rather than "no filter". Absent fields have to be absent, not empty —
   * and a whitespace-only phrase is no phrase.
   */
  it('omits every absent or empty parameter', async () => {
    const members = service.getMembers({ search: '   ' });

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/members'));
    expect(request.request.params.keys()).toEqual([]);
    request.flush({ items: [], total: 0, page: 1, pageSize: 25 });

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

  // Nothing here catches: the screen has to know a block failed so it can keep the row as it was.
  it('rejects rather than swallowing a failed block', async () => {
    const blocked = service.block('m1');

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/block'))).flush(
      { reason: 'is_admin' },
      { status: 409, statusText: 'Conflict' },
    );

    await expect(blocked).rejects.toBeDefined();
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
