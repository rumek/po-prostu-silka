/**
 * Arranges a spec's data through the admin API - never through production code, never ad hoc from
 * a spec - and registers each thing's removal the moment it exists.
 *
 * What cannot be removed stays behind by decision: members and accounts have no delete endpoint, so
 * every member a spec creates is named `E2E <purpose> <ts>` and every address ends @example.test.
 */
import { APIRequestContext, APIResponse, expect } from '@playwright/test';
import { trainerCredentials } from '../credentials';
import { Cleanup } from './cleanup';
import { randomClassStart } from './slots';
import { registerViaApi } from './sessions';

/** How many random slots createClass tries before it gives up on a crowded week. */
const SLOT_ATTEMPTS = 20;

interface PageOf<T> {
  items: T[];
  total: number;
}

export interface MemberRow {
  id: string;
  userId: string | null;
  displayName: string;
  email: string | null;
  roles: string[];
}

export interface CreatedClass {
  id: string;
  name: string;
  startsAt: Date;
  capacity: number;
}

/** A timestamp suffix, unique enough for names and addresses across parallel runs and re-runs. */
export function uniqueSuffix(): string {
  return `${Date.now()}${Math.floor(Math.random() * 1000)}`;
}

/** YYYY-MM-DD for a DateOnly field, in local time. */
function dateOnly(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export class Club {
  constructor(
    private readonly api: APIRequestContext,
    private readonly cleanup: Cleanup,
    private readonly baseURL: string,
  ) {}

  /** A member record with no account. Not removable - see the file header for the naming rule. */
  async createMember(displayName: string): Promise<string> {
    const response = await this.api.post('/api/admin/members', {
      data: {
        displayName,
        phoneNumber: null,
        street: null,
        houseNumber: null,
        postalCode: null,
        city: null,
      },
    });
    expect(response.ok(), `create member: ${await response.text()}`).toBeTruthy();
    return ((await response.json()) as { id: string }).id;
  }

  /**
   * Members who held a booking on a class this test created, filled in by the class's removal. A
   * karnet that paid for a booking keeps pointing at it for good, so it cannot be revoked (the API
   * refuses with `has_active_bookings` or `has_booking_history`) and stays behind on its E2E member.
   */
  private readonly bookedMembers = new Set<string>();

  /**
   * A karnet valid from today for `validDays`. Revoked in cleanup unless a booking ever pointed at
   * it - arrange the karnet BEFORE the class, so the class's removal runs first and records who was
   * booked.
   */
  async issuePass(memberId: string, entries = 5, validDays = 30): Promise<string> {
    const validFrom = new Date();
    const validTo = new Date();
    validTo.setDate(validTo.getDate() + validDays);

    const response = await this.api.post(`/api/admin/members/${memberId}/passes`, {
      data: {
        typeName: 'Karnet E2E',
        validFrom: dateOnly(validFrom),
        validTo: dateOnly(validTo),
        entryCount: entries,
      },
    });
    expect(response.ok(), `issue pass: ${await response.text()}`).toBeTruthy();
    const passId = ((await response.json()) as { id: string }).id;

    this.cleanup.add(`revoke pass ${passId}`, async () => {
      if (!this.bookedMembers.has(memberId)) {
        return this.api.delete(`/api/admin/members/${memberId}/passes/${passId}`);
      }
    });
    return passId;
  }

  /**
   * A class type named `name` and one class of it in a free random slot. Cleanup deletes the class
   * if nobody was ever booked on it; otherwise - bookings made through the UI included - it CANCELS
   * it, since the API keeps a booked class as history. A cancelled class stays behind but frees its
   * slot (the overlap rule counts Scheduled classes only). Then it deactivates the type (types
   * cannot be deleted).
   */
  async createClass(
    name: string,
    options: { instructorMemberId?: string; capacity?: number; durationMinutes?: number } = {},
  ): Promise<CreatedClass> {
    const capacity = options.capacity ?? 5;
    const durationMinutes = options.durationMinutes ?? 30;
    const instructorMemberId = options.instructorMemberId ?? (await this.e2eTrainerId());

    const type = await this.api.post('/api/admin/class-types', {
      data: {
        name,
        description: null,
        defaultDurationMinutes: durationMinutes,
        defaultCapacity: capacity,
      },
    });
    expect(type.ok(), `create class type: ${await type.text()}`).toBeTruthy();
    const classTypeId = ((await type.json()) as { id: string }).id;
    this.cleanup.add(`deactivate class type ${classTypeId}`, () =>
      this.api.post(`/api/admin/class-types/${classTypeId}/deactivate`),
    );

    for (let attempt = 0; attempt < SLOT_ATTEMPTS; attempt++) {
      const startsAt = randomClassStart();
      const created = await this.api.post('/api/admin/classes', {
        data: {
          classTypeId,
          startsAt: startsAt.toISOString(),
          durationMinutes,
          instructorMemberId,
          capacity,
        },
      });

      if (created.status() === 409) {
        const { reason } = (await created.json()) as { reason?: string };
        if (reason === 'time_conflict') {
          continue;
        }
      }
      expect(created.ok(), `create class: ${await created.text()}`).toBeTruthy();

      const id = ((await created.json()) as { id: string }).id;
      this.cleanup.add(`delete class ${id}`, () => this.removeClass(id));
      return { id, name, startsAt, capacity };
    }

    throw new Error(
      `No free class slot in ${SLOT_ATTEMPTS} random attempts - the week is crowded.`,
    );
  }

  /** Books a member as the admin. No cleanup of its own: the class's removal accounts for it. */
  async book(classId: string, memberId: string): Promise<void> {
    const response = await this.api.post(`/api/admin/classes/${classId}/bookings`, {
      data: { memberId },
    });
    expect(response.ok(), `book: ${await response.text()}`).toBeTruthy();
  }

  /** Issues (or re-issues) the invitation code for an accountless member. */
  async issueInvitation(memberId: string): Promise<string> {
    const response = await this.api.post(`/api/admin/members/${memberId}/access-code`);
    expect(response.ok(), `issue access code: ${await response.text()}`).toBeTruthy();
    return ((await response.json()) as { code: string }).code;
  }

  /** Claims `memberId`'s record with a new account, through the API. */
  async registerMember(memberId: string, email: string, password: string): Promise<void> {
    const code = await this.issueInvitation(memberId);
    await registerViaApi(this.baseURL, { email, password, code });
  }

  /** The member whose name or address matches `search` exactly, if any. */
  async findMember(search: string): Promise<MemberRow | undefined> {
    const response = await this.api.get('/api/admin/members', {
      params: { search, pageSize: 20 },
    });
    expect(response.ok(), `list members: ${await response.text()}`).toBeTruthy();
    const page = (await response.json()) as PageOf<MemberRow>;
    return page.items.find((m) => m.email === search || m.displayName === search);
  }

  /** The E2E trainer's member id. trainer.setup.ts guarantees it exists before any spec runs. */
  async e2eTrainerId(): Promise<string> {
    const trainer = await this.findMember(trainerCredentials.email);
    expect(trainer, 'the E2E trainer - trainer.setup.ts creates it').toBeDefined();
    return trainer!.id;
  }

  private async removeClass(classId: string): Promise<APIResponse> {
    const bookings = await this.api.get(`/api/admin/classes/${classId}/bookings`);
    if (bookings.ok()) {
      for (const booking of (await bookings.json()) as { memberId: string }[]) {
        this.bookedMembers.add(booking.memberId);
      }
    }

    const deleted = await this.api.delete(`/api/admin/classes/${classId}`);
    if (deleted.status() !== 409) {
      return deleted;
    }
    return this.api.post(`/api/admin/classes/${classId}/cancel`);
  }
}
