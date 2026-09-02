/**
 * Role names exactly as the API stores and sends them, mirroring `ApplicationRoles` on the server
 * (`src/Domain/ApplicationRoles.cs`). This is a CONTRACT — a mismatch here does not fail loudly, it
 * silently makes a role check return false or a badge not render.
 *
 * Kept as plain string constants rather than an enum because the wire format is a plain string and
 * every consumer compares against `roles: string[]` straight from JSON.
 *
 * NOTE for the server side: the API keeps two role sets for two different jobs — every role that
 * must exist in the database, and the subset that satisfies the ActiveMember policy. The SPA needs
 * neither distinction; it only ever asks "does this account hold role X".
 */
export const ROLES = {
  /** Uses the app: schedule, bookings, own training plan. Every registered member holds it. */
  member: 'User',

  /** Manages members, schedule, training plans, exercises. Seeded at setup, never self-registered. */
  admin: 'Admin',

  /** Runs classes. Granted by an admin to an approved account; additive, and confers nothing alone. */
  trainer: 'Trainer',
} as const;

export type RoleName = (typeof ROLES)[keyof typeof ROLES];
