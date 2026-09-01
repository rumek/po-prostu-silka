import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MemberAdminService } from './member-admin.service';
import { PendingMember } from './member-admin.models';

const MEMBER: PendingMember = {
  id: 'm1',
  email: 'nowy@test.local',
  displayName: 'Nowy Członek',
  createdAt: '2026-09-01T08:00:00+00:00',
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

  // Nothing here catches: the screen has to know an approve failed so it can keep the row.
  it('rejects rather than swallowing a failed approve', async () => {
    const approved = service.approve('m1');

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/approve'))).flush(
      { reason: 'not_pending' },
      { status: 409, statusText: 'Conflict' },
    );

    await expect(approved).rejects.toBeDefined();
  });
});
