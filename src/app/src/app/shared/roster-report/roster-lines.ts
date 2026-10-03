/**
 * One line of a roster report or a member's gaps (S-37): a person, one reason, and every class date that
 * reason covers — "Anna Kowalska — brak karnetu: 4.11, 11.11, 18.11" rather than one line per class.
 */
export interface RosterLine {
  memberId: string;
  memberName: string;
  reason: string;
  /** ISO instants, in the order they arrived (the API sends classes in start order). */
  startsAt: string[];
}

interface Groupable {
  memberId: string;
  memberName: string;
  reason: string;
  startsAt: string;
}

/**
 * Groups by member and reason, keeping first-seen order: members as the API listed them, and within a
 * member, reasons in the order their first class comes up.
 */
export function groupRosterLines(items: readonly Groupable[]): RosterLine[] {
  const lines = new Map<string, RosterLine>();

  for (const item of items) {
    const key = `${item.memberId}\u0000${item.reason}`;
    const line = lines.get(key);

    if (line) {
      line.startsAt.push(item.startsAt);
    } else {
      lines.set(key, {
        memberId: item.memberId,
        memberName: item.memberName,
        reason: item.reason,
        startsAt: [item.startsAt],
      });
    }
  }

  return [...lines.values()];
}

/**
 * The success half of a roster report (S-37) — the count rides the screen's toast, the skips go to the
 * panel. One sentence for every trigger (create, duplicate, karnet), so the screens cannot word it four
 * ways.
 */
export function rosterBookedSentence(booked: number): string {
  return `Zapisano ze składu grupy: ${booked}.`;
}
