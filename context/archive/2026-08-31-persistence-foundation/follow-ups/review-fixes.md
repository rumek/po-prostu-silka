# Review follow-ups — persistence-foundation

Queued from `reviews/impl-review.md` during triage on 2026-08-31. Both were deferred by the
reviewer, not dismissed.

## F2 — Add a composition-root carve-out to the layering rule

- **Files**: `AGENTS.md`, `CLAUDE.md`
- **Why**: `src/Program.cs:1,14` uses EF Core (`using Microsoft.EntityFrameworkCore;` +
  `AddDbContext<AppDbContext>`), but it sits at `src/` root, outside the three layer folders, and
  both docs say `Infrastructure` is the *only* layer that may reference EF Core. The code is
  correct — `Domain/` and `Application/` are verifiably EF-free — so the fix is to the rule text.
- **What to do**: add one line stating that the composition root (`Program.cs`) may reference EF
  Core solely to register `AppDbContext`. Without it, every future implementation review re-flags
  this same line.

## F3 — Record the three unplanned-but-benign additions as a plan addendum

- **File**: `context/changes/persistence-foundation/plan.md`
- **Why**: three things exist that no plan contract names, all benign. Documenting them stops the
  next review from re-investigating.
- **What to do**: append a short addendum covering
  1. `src/Application/README.md` — placeholder so git can track the otherwise-empty `Application/`
     layer the plan did require.
  2. `docker-compose.yml:19,24-33` — `MSSQL_PID: "Developer"` and the `sqlcmd` healthcheck, both
     local-dev-only conveniences beyond the stated image/EULA/password/port/volume contract.
  3. `.github/workflows/deploy.yml:96` — `--project src/po-prostu-silka.csproj` on
     `dotnet ef database update`, omitted from the contract text but needed to resolve the csproj
     from the repo root.
- **Reviewer note**: deferred until real entities exist (F-02 onward), so the addendum can be
  written once against a settled layout.
