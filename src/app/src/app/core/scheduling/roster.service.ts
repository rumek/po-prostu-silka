import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { GroupRosterView, RosterChange, TrainerGroup } from './roster.models';

/**
 * Group rosters (S-37). TrainerOrAdmin: an admin manages any group's roster, a trainer the roster of a
 * group they instruct an upcoming class of — the API answers anything else with a 403. Nothing here
 * catches; a refusal has to reach the screen.
 */
@Injectable({ providedIn: 'root' })
export class RosterService {
  private readonly http = inject(HttpClient);

  get(groupId: string): Promise<GroupRosterView> {
    return firstValueFrom(this.http.get<GroupRosterView>(this.roster(groupId)));
  }

  /** Adds a member and books them into the group's upcoming classes. */
  add(groupId: string, memberId: string): Promise<RosterChange> {
    return firstValueFrom(this.http.post<RosterChange>(this.roster(groupId), { memberId }));
  }

  /** Removes a member and releases their future non-makeup bookings in the group. */
  remove(groupId: string, memberId: string): Promise<GroupRosterView> {
    return firstValueFrom(
      this.http.delete<GroupRosterView>(`${this.roster(groupId)}/${encodeURIComponent(memberId)}`),
    );
  }

  /** "Uzupełnij zapisy": retries every gap of the roster. */
  sync(groupId: string): Promise<RosterChange> {
    return firstValueFrom(this.http.post<RosterChange>(`${this.roster(groupId)}/sync`, {}));
  }

  /** The trainer's "Grupy": the groups whose upcoming classes the caller instructs. */
  trainerGroups(): Promise<TrainerGroup[]> {
    return firstValueFrom(this.http.get<TrainerGroup[]>('/api/trainer/groups'));
  }

  private roster(groupId: string): string {
    return `/api/groups/${encodeURIComponent(groupId)}/roster`;
  }
}
