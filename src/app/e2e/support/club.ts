/**
 * Arranges a spec's data through the admin API - never through production code, never ad hoc from
 * a spec - and registers each thing's removal the moment it exists.
 *
 * What cannot be removed stays behind by decision: members and accounts have no delete endpoint, so
 * every member a spec creates is named `E2E <purpose> <ts>` and every address ends @example.test.
 * The same goes for a karnet that paid for a booking, a booked class (cancelled, or - once started -
 * kept as attendance history), a plan (no delete endpoint either), and an exercise or group
 * (deactivated, never deleted).
 */
import { APIRequestContext, APIResponse, expect } from '@playwright/test';
import { trainerCredentials } from '../credentials';
import { Cleanup } from './cleanup';
import { randomClassStart, randomPastClassStart } from './slots';
import { registerViaApi } from './sessions';

/** How many random slots createClass and startClass try before they give up on a crowded week. */
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
  classGroupId: string;
  durationMinutes: number;
  instructorMemberId: string;
}

/** The optional fields of a library exercise - only the name is required (ExerciseValidator). */
export interface ExerciseFields {
  description?: string;
  muscleGroup?: string;
  preparation?: string;
  startingPosition?: string;
  execution?: string;
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
   * A karnet valid from today (or `validFromDaysAgo` days back - a class moved into the past by
   * startClass must fall inside it) until `validDays` from today. Revoked in cleanup unless a booking
   * ever pointed at it - arrange the karnet BEFORE the class, so the class's removal runs first and
   * records who was booked.
   */
  async issuePass(
    memberId: string,
    entries = 5,
    validDays = 30,
    options: { validFromDaysAgo?: number } = {},
  ): Promise<string> {
    const validFrom = new Date();
    validFrom.setDate(validFrom.getDate() - (options.validFromDaysAgo ?? 0));
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
   * A group named `name` and one class of it in a free random slot. Cleanup deletes the class
   * if nobody was ever booked on it; otherwise - bookings made through the UI included - it CANCELS
   * it, since the API keeps a booked class as history. A cancelled class stays behind but frees its
   * slot (the overlap rule counts Scheduled classes only). A class that has STARTED (startClass) can
   * be neither, and stays behind as it is: attendance history, in a past slot. Then it deactivates
   * the group (groups cannot be deleted).
   */
  async createClass(
    name: string,
    options: { instructorMemberId?: string; capacity?: number; durationMinutes?: number } = {},
  ): Promise<CreatedClass> {
    const capacity = options.capacity ?? 5;
    const durationMinutes = options.durationMinutes ?? 30;
    const instructorMemberId = options.instructorMemberId ?? (await this.e2eTrainerId());

    const group = await this.api.post('/api/admin/class-groups', {
      data: {
        name,
        description: null,
        defaultDurationMinutes: durationMinutes,
        defaultCapacity: capacity,
      },
    });
    expect(group.ok(), `create group: ${await group.text()}`).toBeTruthy();
    const classGroupId = ((await group.json()) as { id: string }).id;
    this.cleanup.add(`deactivate group ${classGroupId}`, () =>
      this.api.post(`/api/admin/class-groups/${classGroupId}/deactivate`),
    );

    for (let attempt = 0; attempt < SLOT_ATTEMPTS; attempt++) {
      const startsAt = randomClassStart();
      const created = await this.api.post('/api/admin/classes', {
        data: {
          classGroupId,
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
      return { id, name, startsAt, capacity, classGroupId, durationMinutes, instructorMemberId };
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

  /**
   * Makes a class one that has STARTED, without waiting for it: moves its start to a random free slot
   * in the past week through the admin edit, which - unlike creation - accepts a past start
   * (correcting a class that already ran). Retries on `time_conflict` like createClass.
   *
   * Call it after every booking: a started class takes none (`class_started`). It stays behind after
   * the test - see createClass.
   */
  async startClass(created: CreatedClass): Promise<CreatedClass> {
    for (let attempt = 0; attempt < SLOT_ATTEMPTS; attempt++) {
      const startsAt = randomPastClassStart();
      const moved = await this.api.put(`/api/admin/classes/${created.id}`, {
        data: {
          classGroupId: created.classGroupId,
          startsAt: startsAt.toISOString(),
          durationMinutes: created.durationMinutes,
          instructorMemberId: created.instructorMemberId,
          capacity: created.capacity,
        },
      });

      if (moved.status() === 409) {
        const { reason } = (await moved.json()) as { reason?: string };
        if (reason === 'time_conflict') {
          continue;
        }
      }
      expect(moved.ok(), `start class: ${await moved.text()}`).toBeTruthy();
      return { ...created, startsAt };
    }

    throw new Error(
      `No free past slot in ${SLOT_ATTEMPTS} random attempts - the past week is crowded.`,
    );
  }

  /**
   * A library exercise, retired (deactivated) in cleanup - exercises cannot be deleted, and a plan
   * that prescribes one still reads after it is retired. Give it a unique `E2E …` name: names are
   * unique among active exercises.
   */
  async createExercise(name: string, fields: ExerciseFields = {}): Promise<string> {
    const response = await this.api.post('/api/admin/exercises', {
      data: {
        name,
        description: fields.description ?? null,
        muscleGroup: fields.muscleGroup ?? null,
        difficulty: null,
        equipment: null,
        preparation: fields.preparation ?? null,
        startingPosition: fields.startingPosition ?? null,
        execution: fields.execution ?? null,
        videoUrl: null,
      },
    });
    expect(response.ok(), `create exercise: ${await response.text()}`).toBeTruthy();
    const id = ((await response.json()) as { id: string }).id;
    this.cleanup.add(`deactivate exercise ${id}`, () =>
      this.api.post(`/api/admin/exercises/${id}/deactivate`),
    );
    return id;
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

  private async removeClass(classId: string): Promise<APIResponse | void> {
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

    const cancelled = await this.api.post(`/api/admin/classes/${classId}/cancel`);
    if (cancelled.status() === 409) {
      // A class that has started is attendance history: it stays behind by decision (startClass).
      const { reason } = (await cancelled.json()) as { reason?: string };
      if (reason === 'class_started') {
        return;
      }
    }
    return cancelled;
  }
}
