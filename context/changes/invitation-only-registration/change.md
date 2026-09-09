---
change_id: invitation-only-registration
title: An account is created only by invitation — code-gated registration, shortened form
status: implementing
created: 2026-09-09
updated: 2026-09-09
archived_at: null
---

## Notes

M-5's only slice (roadmap S-17). Registration stops being a public door: the club's desk enters the
person into the records, and the only way to attach a login to those records is an invitation code
handed over in person.

Settled with the user before planning; the anchors are `IR-01`–`IR-05` in
`context/foundation/roadmap.md`:

1. An admin records a member with no email address. That person gets no email and no push — an
   accepted consequence, not a defect.
2. `/register` is reachable ONLY with an `invitationCode` query parameter; without one the visitor is
   redirected to `/login`. No screen in the app links to registration.
3. The code arrives in the query string, prefills the field, and the field is readonly.
4. Registration asks for an email address and a password, and nothing else. Display name, phone and
   postal address come from the member record the code attaches to.
5. Registration without a valid code is impossible, and the refusal lives in the API, not only in the
   SPA. Unknown / expired / revoked / already-used stay collapsed into one answer (S-14's
   account-enumeration reasoning).

**Naming:** the query parameter is `invitationCode`; the API contract keeps `memberCode`. Two names
for one thing, deliberately — no shipped contract moves.

**Not in scope:** issuing the invitation. The admin surface that produces, shows and revokes a member
code shipped with S-14 and is untouched.

**Known cleanup this slice inherits:** `register.html`'s header still says the account awaits staff
approval — stale since S-16 removed approval, and the form it sits above is being rewritten anyway.
