import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MemberAdminService } from '../../../core/admin/member-admin.service';
import {
  IssuePassRequest,
  MemberDetail,
  MembershipPassView,
} from '../../../core/admin/member-admin.models';
import { membershipPassFailureMessage } from '../../../core/admin/membership-pass-failure';

/**
 * Bounds mirrored from MembershipPassRules (src/Application/Members/MembershipPassRules.cs).
 *
 * MIRRORED, NOT AUTHORITATIVE. The server refuses the same values with its own message; these exist
 * so the admin is told before the round trip rather than after it. Keep the two in step — a form that
 * accepts what the API rejects is a worse experience than one with no validation at all.
 */
const TYPE_NAME_MAX_LENGTH = 100;
const MIN_ENTRY_COUNT = 1;
const MAX_ENTRY_COUNT = 500;

/**
 * One member's karnety: the history, and the form that issues the next one (S-16, MP-04..MP-06).
 *
 * <p>
 * A SCREEN OF ITS OWN rather than a section on the member form. The two answer different questions —
 * who this person is, versus what they are entitled to — and the karnet is the thing the club touches
 * repeatedly while the contact details are written once. Folding it in would also mean one submit
 * button for two unrelated writes.
 * </p>
 *
 * <p>
 * ENTRIES LEFT IS A READING, NOT A FACT. The API derives it from active bookings, so anything that
 * books or releases a spot changes it — which is why every action here refetches the whole history
 * instead of patching the row it just wrote. See MembershipPass in the Domain for why there is no
 * counter to patch in the first place.
 * </p>
 *
 * <p>
 * The `generation` fencing counter is the same guard the other admin screens carry: two responses to
 * the same list can land in either order, and the loser must not write back.
 * </p>
 */
@Component({
  imports: [DatePipe, ReactiveFormsModule, RouterLink],
  selector: 'app-member-passes',
  styleUrl: './member-passes.scss',
  templateUrl: './member-passes.html',
})
export class MemberPasses implements OnInit {
  private readonly members = inject(MemberAdminService);
  private readonly route = inject(ActivatedRoute);

  protected readonly memberId = signal<string>('');
  protected readonly member = signal<MemberDetail | null>(null);

  protected readonly passes = signal<MembershipPassView[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  private generation = 0;

  /** The pass being edited, or null while the form is issuing a new one. */
  protected readonly editingId = signal<string | null>(null);

  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  /** The row whose revoke is in flight, so only that row's button shows a busy state. */
  protected readonly revokingId = signal<string | null>(null);

  protected readonly editing = computed(() => this.editingId() !== null);

  /**
   * The pass covering today, which the header states in one line — the admin's first question at the
   * desk is almost always "is this person's karnet live", and making them read the table for it would
   * be a worse answer than a sentence.
   */
  protected readonly current = computed(() => this.passes().find((p) => p.coversToday) ?? null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    typeName: ['', [Validators.required, Validators.maxLength(TYPE_NAME_MAX_LENGTH)]],
    validFrom: ['', [Validators.required]],
    validTo: ['', [Validators.required]],
    entryCount: [
      10,
      [Validators.required, Validators.min(MIN_ENTRY_COUNT), Validators.max(MAX_ENTRY_COUNT)],
    ],
  });

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (id === null) {
      this.loadFailed.set(true);
      this.loading.set(false);
      return;
    }

    this.memberId.set(id);

    // The member's name, so the screen says WHOSE karnety these are. Failure here is not fatal —
    // the history is still readable and useful — so it is deliberately not folded into loadFailed.
    try {
      this.member.set(await this.members.getMember(id));
    } catch {
      this.member.set(null);
    }

    await this.load();
  }

  protected async load(): Promise<void> {
    const generation = ++this.generation;

    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      const rows = await this.members.getPasses(this.memberId());

      if (generation !== this.generation) {
        return;
      }

      this.passes.set(rows);
    } catch {
      if (generation !== this.generation) {
        return;
      }

      this.passes.set([]);
      this.loadFailed.set(true);
    } finally {
      if (generation === this.generation) {
        this.loading.set(false);
      }
    }
  }

  /**
   * Loads a pass into the form for correction.
   *
   * The date inputs take `YYYY-MM-DD`, which is exactly the shape the API already sends — so the
   * values go across UNPARSED. Round-tripping them through `Date` would reintroduce the timezone
   * shift `DateOnly` exists to avoid, and could move a karnet's last day by one.
   */
  protected edit(pass: MembershipPassView): void {
    this.editingId.set(pass.id);
    this.error.set(null);

    this.form.setValue({
      typeName: pass.typeName,
      validFrom: pass.validFrom,
      validTo: pass.validTo,
      entryCount: pass.entryCount,
    });
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
    this.error.set(null);
    this.form.reset({ typeName: '', validFrom: '', validTo: '', entryCount: 10 });
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    // Checked before the round trip so the admin sees the whole rule rather than the server's
    // one-field answer. The API enforces it too — this is a courtesy, never the boundary.
    const { validFrom, validTo } = this.form.getRawValue();
    if (validTo < validFrom) {
      this.error.set(membershipPassFailureMessage('invalid_range'));
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    const request: IssuePassRequest = this.form.getRawValue();
    const passId = this.editingId();

    try {
      if (passId === null) {
        await this.members.issuePass(this.memberId(), request);
      } else {
        await this.members.updatePass(this.memberId(), passId, request);
      }

      this.cancelEdit();

      // Refetched rather than patched: entries used is derived server-side, and the row this write
      // returned is already stale the moment anybody books.
      await this.load();
    } catch (failure) {
      this.error.set(
        membershipPassFailureMessage(
          failure instanceof HttpErrorResponse ? failure.error?.reason : null,
        ),
      );
    } finally {
      this.submitting.set(false);
    }
  }

  protected async revoke(pass: MembershipPassView): Promise<void> {
    this.revokingId.set(pass.id);
    this.error.set(null);

    try {
      await this.members.revokePass(this.memberId(), pass.id);

      // Clear the form if it was editing the pass that just went away, so the admin is not left
      // looking at a form that would recreate it.
      if (this.editingId() === pass.id) {
        this.cancelEdit();
      }

      await this.load();
    } catch (failure) {
      this.error.set(
        membershipPassFailureMessage(
          failure instanceof HttpErrorResponse ? failure.error?.reason : null,
        ),
      );
    } finally {
      this.revokingId.set(null);
    }
  }
}
