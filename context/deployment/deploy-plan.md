---
project: po-prostu-silka
platform: Azure App Service (Linux, B1)
status: live
last_updated: 2026-08-31
---

## What this is

The audit trail for the first deployment of po-prostu-silka, executed from the approved Plan Mode plan (`context/foundation/infrastructure.md` is the underlying platform decision). This records what was supposed to happen and what actually happened, so a future session — or a live run gone sideways — has ground truth instead of having to reconstruct it from chat history.

## Scope of this deployment

Deploy the ASP.NET Core API and the Angular SPA (as a static bundle served from the API's `wwwroot`) to a single Azure App Service (Linux, B1). Azure SQL Database is deliberately **not** provisioned in this deployment — there is no EF Core/connection string in the app yet, so a database would sit unused. It's planned for the deploy that introduces Identity/EF Core.

> **Superseded 2026-08-31 by change `persistence-foundation` (roadmap F-01).** Azure SQL is now provisioned and the app connects to it; migrations run in CI before each deploy. See "## Persistence foundation (F-01)" at the end of this file. The paragraph above is kept as the original record of what the first deployment intended.

## Status: live — first deployment verified end-to-end

Steps A–F are complete. Live URL: **https://po-prostu-silka.azurewebsites.net**

**Note on the blocker below**: it did get resolved, but not the way either listed option assumed. The `kr@anbast.com` / BizSpark login was abandoned; the user instead logged into a different personal account (`rumianowski@hotmail.com`) via `az login`, which initially showed zero subscriptions (`az account list --all`) until `az account list --refresh` surfaced one ("Subskrypcja platformy Azure 1", `1b1298d8-ca6a-4a57-a189-192ff31fbd3a`) that the CLI's cache hadn't picked up yet. Worth remembering: a subscription that exists but doesn't show up in `az account list` may just need `--refresh`, not necessarily a portal-side fix.

### A. App made deployable — DONE

- Retargeted `src/po-prostu-silka.csproj` from `net10.0` with an RC-vintage `Microsoft.AspNetCore.OpenApi` (`10.0.0-rc.2.25502.107`, carrying HIGH-severity transitive advisory GHSA-v5pm-xwqc-g5wc) to GA `Microsoft.AspNetCore.OpenApi 10.0.11`. Verified clean via `dotnet list package --vulnerable`.
- Installed the GA .NET 10 SDK (`10.0.400`) locally via winget; added `global.json` pinning to it (`rollForward: latestFeature`).
- Switched `src/app/angular.json`'s build `outputMode` from `"server"` (SSR) to `"static"` — this deploy drops SSR. Verified build output lands at `src/app/dist/app/browser/` (not the commonly assumed `dist/app/browser` at the project root — Angular nests it one level under the project name).
- `src/Program.cs`: added `UseDefaultFiles()` / `UseStaticFiles()` / `MapFallbackToFile("index.html")` to serve the SPA from `wwwroot`; moved `UseHttpsRedirection()` to run only in `Development` — Azure App Service Linux terminates TLS at the edge and forwards plain HTTP internally, so an unconditional redirect fights the reverse proxy. Production HTTPS enforcement is via the App Service "HTTPS Only" site setting (step C.10, not yet applied — see blocker).
- `.gitignore`: uncommented/rewrote the `wwwroot/` rule (it was present but commented out), and added `src/app/dist/`, `src/app/.angular/`, `publish-test/` — all of this is CI-regenerated build output, never committed.
- Verified locally end-to-end: `dotnet publish` output is clean (no leaked Angular source — the csproj carried `<Compile/Content/EmbeddedResource/None Remove="app\**" />` excludes; S-18 removed them, because `src/app/` is no longer inside any project directory), and a local run of the published app returned 200 on `/`, `/weatherforecast`, and an arbitrary unmapped route (SPA fallback).

### B. GitHub repo — DONE

- Repo: `rumek/po-prostu-silka` (created manually by the user; confirmed reachable before pushing).
- Local branch renamed `master` → `main` to match the deploy workflow's trigger branch and `infrastructure.md`'s "merges to main deploy" operational story.
- Remote `origin` added, initial commit made (includes the fixes above plus this session's skill/context files), pushed to `origin/main`.

### C. Azure resources — DONE

Subscription: **"Subskrypcja platformy Azure 1"** (`1b1298d8-ca6a-4a57-a189-192ff31fbd3a`), account `rumianowski@hotmail.com` — not the BizSpark subscription originally checked (see the note above).

```
az webapp list-runtimes --os-type linux              # confirmed "DOTNETCORE:10.0" is available
az group create -n pps-rg -l polandcentral            # ✅ created
az appservice plan create -n pps-plan -g pps-rg --sku B1 --is-linux   # ✅ created
az webapp create -n po-prostu-silka -g pps-rg -p pps-plan --runtime "DOTNETCORE:10.0"   # ✅ created, po-prostu-silka.azurewebsites.net
az webapp config set -n po-prostu-silka -g pps-rg --always-on true    # ✅ confirmed AlwaysOn=True
az webapp update -n po-prostu-silka -g pps-rg --https-only true       # ✅ confirmed HttpsOnly=True
```

Note: `az appservice plan create` reported a `FreeOfferExpirationTime` of **2026-09-29** — the B1 plan appears to be running under a trial credit for its first ~30 days. Worth checking the Azure portal billing view before that date to understand what happens after (likely reverts to normal B1 billing, ~$13/mo, but verify rather than assume).

### D. CI/CD — DONE

- `.github/workflows/deploy.yml` — triggers on push to `main` (and manually via `workflow_dispatch`), builds Angular static, stages it into `wwwroot`, runs `dotnet publish`, deploys via `azure/webapps-deploy@v3` using the `AZURE_WEBAPP_PUBLISH_PROFILE` secret.
- Publish profile fetched via `az webapp deployment list-publishing-profiles --xml` to a local-only, gitignored file; user copied it into the repo's `Settings → Secrets and variables → Actions` as `AZURE_WEBAPP_PUBLISH_PROFILE`; local file deleted immediately after.
- Auth approach is publish-profile, not OIDC: the local Azure CLI (`2.35.0`) predates federated-credential support, and GitHub CLI isn't installed to script around it. `infrastructure.md`'s risk register explicitly accepts this as an MVP fallback — revisit once the CLI is upgraded.

### E. Verification — DONE

- GitHub Actions run [#2](https://github.com/rumek/po-prostu-silka/actions) completed `success`.
- Kudu deployment API confirmed the OneDeploy on the Azure side completed with no errors.
- **First curl pass after the deploy showed Azure's default `hostingstart.html` placeholder** (root 200 but wrong content; `/weatherforecast` and SPA-fallback both 404) even though the deploy itself reported success and the correct files were confirmed present in `/home/site/wwwroot` via the Kudu VFS API. An explicit `az webapp restart -n po-prostu-silka -g pps-rg` resolved it — **note this for the next deploy**: the very first zip/OneDeploy onto a freshly-created App Service may need an explicit restart before the new app process actually takes over from the platform's placeholder; don't assume a "success" deploy status means the site is actually serving the new app without checking.
- Post-restart, all three checks passed: `/` → 200 with `<title>App</title>` (the Angular shell, not the placeholder), `/weatherforecast` → 200, an arbitrary unmapped route → 200 (confirms `MapFallbackToFile` SPA routing works in production).

### F. This file — kept as the running audit trail across the whole deploy, from the Azure-account blocker through to the verified live result.

## Known follow-ups (not blocking, but don't lose track)

- ~~Azure CLI locally is `2.35.0` (~2022) — very old. No OIDC support, missing newer command surface. Worth upgrading before the next infra-touching session.~~ **RESOLVED 2026-08-31 by `notification-delivery-foundation`.** Upgraded to `2.89.1`. The prediction was accurate and it bit exactly as described: 2.35.0 capped the `communication` extension at a version with no `email` subgroup, so ACS could not be provisioned until the CLI was upgraded mid-phase. This also unblocks OIDC federated credentials for CI (still using a publish profile) and clears the Graph API deprecation that broke `az role assignment list` during F-02.
- GitHub CLI (`gh`) is not installed — all GitHub-side operations (secrets, PR checks) went through the web UI / plain git. Installing it would remove a recurring manual step.
- ~~`dotnet restore` on this project pulls from private organizational NuGet feeds configured in a machine-level NuGet.Config, in addition to nuget.org... worth an explicit public-only `nuget.config` in the repo before that happens.~~ **RESOLVED 2026-08-31 by `persistence-foundation`.** A repo-root `nuget.config` with `<clear />` now pins nuget.org only. The prediction was accurate: at the time EF Core was added, `dotnet package search` reported two extra active sources on the dev machine (a local artifacts folder and the Visual Studio offline packages). Keep the `<clear />` — without it those sources are merged in rather than replaced.
- ~~Azure SQL Database is intentionally deferred to the deploy that introduces EF Core/Identity — don't forget to also enable Always On and HTTPS Only at that point.~~ **RESOLVED 2026-08-31 by `persistence-foundation`** — see the F-01 section at the end of this file. Always On and HTTPS Only were re-verified and were already `true`; no change was needed.
- **Still open — Managed Identity for Azure SQL.** The app authenticates with SQL auth and a password stored in App Service settings. Managed Identity would remove the credential entirely; deferred as post-MVP because the local Azure CLI predated the tooling and the Entra plumbing risked burning the session. **The CLI blocker is now gone** (upgraded to 2.89.1 above), so this is newly actionable — only the post-MVP scheduling call remains.

---

## Persistence foundation (F-01) — 2026-08-31

Change: `context/changes/persistence-foundation/`. Adds Azure SQL, EF Core, and migration-on-deploy.
This section supersedes the "Azure SQL deliberately deferred" note in "Scope of this deployment" above.

### Azure resources added

| Resource | Value |
| --- | --- |
| SQL server | `pps-sql.database.windows.net` (`pps-rg`, polandcentral) |
| Admin login | `ppsadmin` (SQL auth; password in the owner's password manager, nowhere in this repo) |
| Database | `pps-db` — **Basic DTU**, 5 DTU, 2 GB cap |
| Firewall | `AllowAzureServices` (0.0.0.0 sentinel — covers App Service outbound) and `DevWorkstation` |
| App Service setting | Connection string named `Default`, type **`SQLAzure`** |

**The name and type of the connection string are both load-bearing.** App Service exposes it as
`SQLAZURECONNSTR_Default`, which ASP.NET Core's default config provider maps back onto
`ConnectionStrings:Default`. A plain app setting (`az webapp config appsettings set`) does NOT produce
that mapping and `GetConnectionString("Default")` would return null.

Basic DTU is deliberate, not a default: `infrastructure.md` rejects the free serverless tier for this
workload (a background poller wakes it, exhausts the 100k vCore-second quota, and the database pauses
until the next month).

### CI identity added

- Service principal **`pps-ci`**, role `contributor`, **scoped to the `pps-rg` resource group only** —
  never the subscription. Created because the pre-existing publish-profile credential authenticates to
  App Service and nothing else; it cannot reach Azure SQL or manage firewall rules.
- New GitHub Actions secrets (names only — values live in GitHub):
  - `AZURE_CREDENTIALS` — the `--sdk-auth` JSON for `pps-ci`, consumed by `azure/login@v2`
  - `AZURE_SQL_CONNECTION_STRING` — used by `dotnet ef database update`
- The existing `AZURE_WEBAPP_PUBLISH_PROFILE` is unchanged and still does the deploy.

### Migration pipeline (`.github/workflows/deploy.yml`)

Steps run **before** `azure/webapps-deploy`, so a failed migration aborts the run with the previous
code still serving: install `dotnet-ef` → generate an idempotent script → upload it as a run artifact →
`azure/login` → open a JIT firewall rule for the runner IP → `dotnet ef database update` → delete the
rule (`if: always()`) → deploy.

Two decisions worth not re-litigating:

- **GitHub-hosted runners are not "Azure services."** The `0.0.0.0` firewall sentinel covers the App
  Service outbound path but not CI, which is why each run opens and closes its own rule, named by run id
  so concurrent runs cannot collide. The `if: always()` guard on the cleanup is load-bearing — without
  it, a failed migration leaves the runner's IP permanently allowed on a database holding personal data.
- **Migrations are applied with `dotnet ef`, not `azure/sql-action`.** The runner is `ubuntu-latest`;
  sql-action's `.sql` support on Linux is not established and current ubuntu images no longer ship
  `mssql-tools`. The EF tool is installed in the same job anyway. Tradeoff accepted: the uploaded
  `migrations.sql` artifact is evidence of intent rather than the literal executed bytes — both derive
  from the same migration set in the same commit, so they cannot diverge in content.

### Gotchas confirmed or discovered this change

- **Git Bash mangles `/subscriptions/...` arguments** into `C:/Program Files/Git/subscriptions/...`.
  This silently half-created the `pps-ci` service principal (identity made, role assignment failed).
  Prefix such commands with `MSYS_NO_PATHCONV=1`. Same fix applies to `docker exec /opt/...` paths.
- **`dotnet-ef` was installed at 7.0.9** on the dev machine and had to be updated to 10.0.11; an EF 7
  tool fails against this .NET 10 project.
- **`dotnet restore` now pins nuget.org only** via a repo-root `nuget.config` with `<clear />`. This
  machine had two private feeds active (an artifacts folder and VS offline packages) that CI cannot
  reach — the failure predicted in "Known follow-ups" below, closed before it could bite.
- **A deleted endpoint cannot return 404** while `MapFallbackToFile` is registered — it serves the SPA
  shell with 200 for every unmatched route. Check the response body, not the status code. This also
  means `/health` returning 200 is not proof the new build is live; only the body `Healthy` is.
- The **restart-after-deploy gotcha** recorded above did **not** recur this time; the deploy served the
  new build on its own after ~6-7 minutes.

### Rollback note

EF migrations do **not** roll back with an artifact redeploy (no slots on B1). Migrations must ship a
working `Down`, and destructive changes lag one release behind the code that stops needing them.

## Notification delivery foundation (F-03) — 2026-08-31, Phase 1

### Azure CLI upgraded

The local Azure CLI was **2.35.0 (~2022)** — the standing follow-up below. It capped the
`communication` extension at `1.3.0`, which has `az communication create` but **no `email`
subgroup at all**, so the Email Service and managed domain were not creatable. Upgraded via
`winget upgrade --id Microsoft.AzureCLI -e` to **2.89.1**, then `az extension update --name
communication` moved the extension to **1.14.0**, where `az communication email` exists.

Note for future sessions: after the upgrade the new binary is at
`/c/Program Files/Microsoft SDKs/Azure/CLI2/wbin` and an already-open shell will not see it until
its PATH is refreshed.

### Azure resources added

| Resource | Value |
| --- | --- |
| Resource provider | `Microsoft.Communication` — was `NotRegistered`; registered this change (subscription-scoped, ~1 min) |
| Email Service | `pps-email` (`pps-rg`, location `Global`, **data at rest: `Europe`**) |
| Email domain | `AzureManagedDomain` under `pps-email` — `domainManagement: AzureManaged`, verification `Verified` (SPF verified too) |
| Sender domain | `a47eab51-bc3d-4b51-92c5-43d2a40802b8.azurecomm.net` |
| Sender address | `DoNotReply@a47eab51-bc3d-4b51-92c5-43d2a40802b8.azurecomm.net` |
| Sender display name | `Po Prostu Siłka` (set 2026-09-25; the `donotreply` sender username's `displayName`, default `DoNotReply`) |
| Communication Service | `pps-acs` (`pps-rg`, location `Global`, data location `Europe`), linked to the managed domain |

### App Service settings added (names only — values are secrets)

`Acs__ConnectionString`, `Acs__SenderAddress`, `VapidKeys__PublicKey`, `VapidKeys__PrivateKey`,
`VapidKeys__Subject`. Double-underscore nesting, matching the `AdminSeed__*` convention. Setting
them restarts the app; the live site returned 503 briefly, then `Healthy`.

### The managed-domain decision

`roadmap.md` and `infrastructure.md:77` both treat ACS sender-domain verification as the milestone's
**#1 blocker** — multi-day, DNS-gated, provider-side lead time that "belongs in week 1, not week 3".
An **Azure Managed Domain** sidesteps it entirely: no DNS records, provisioned and `Verified` in well
under a minute. That removed the blocker from the critical path rather than waiting it out.

The trade accepted: an unbranded `*.azurecomm.net` sender, lower send limits, and weaker
deliverability than a verified custom domain. Against a "no missed cancellations" guardrail this is a
real risk — **if members report mail landing in spam, the custom-domain migration moves from optional
to required.** Switching later changes only `Acs__SenderAddress`, not application code.

### Gotchas confirmed or discovered this change

- **Git Bash mangles Azure resource IDs.** `--linked-domains /subscriptions/...` was rewritten to
  `C:/Program Files/Git/subscriptions/...` and rejected with `LinkedInvalidPropertyId`. Prefix such
  commands with `MSYS_NO_PATHCONV=1`. The same applies to `docker exec` paths.
- `az communication email` is still marked **preview** on extension 1.14.0 — it warns on every call.
- **The sender's display name is an Azure setting, not app configuration.** The managed domain
  allows no other username, but its one sender's display name can be changed:
  `az communication email domain sender-username update --email-service-name pps-email
  --domain-name AzureManagedDomain -g pps-rg --sender-username donotreply --display-name "Po Prostu Siłka"`.
  The Windows console prints the `ł` as `�`; the stored value is correct (read back through ARM).
  A custom domain's new sender needs the same setting again.
- VAPID keys are a P-256 keypair, base64url-encoded raw (87-char public point, 43-char private
  scalar). Generated with `openssl ecparam -genkey -name prime256v1`; the local Python had no
  `cryptography` module.
- **Rotating the VAPID keypair invalidates every stored push subscription.** Members would silently
  stop receiving push until they re-subscribe. Treat it as a one-time value.

### Follow-up opened by this change

- **Custom sender domain.** Deferred deliberately (see above). Requires DNS access to the chosen
  domain and multi-day provider-side verification; closing it is a change to
  `Acs__SenderAddress` only.

### Transport live (F-03 phases 2-4)

| Item | Value |
| --- | --- |
| Outbox table | `OutboxMessages` — one row per recipient per channel, holding the already-rendered message |
| Push table | `PushSubscriptions` — unique on `Endpoint`, cascade-deleted with the member |
| Claim index | `IX_OutboxMessages_Status_NextAttemptAt` — the worker's claim predicate, the only query running every polling interval |
| Worker | `OutboxDeliveryWorker`, 15s poll, batch 20, 5-minute lease, 5 attempts, backoff 1m/5m/15m/1h/4h |
| Retention | `Sent` rows pruned after 30 days; `Failed` rows kept as the diagnostic record |
| Observability | Heartbeat log line per pass with pending/claimed/failed counts; `/health` reports `Degraded` past 10 failed rows |
| First consumer | Account-approved notification (FR-021) — one email row plus one push row per registered device |

**`SchemaMarkers` is gone.** F-01 created it to prove the pipeline; F-02 deleted the C# and left the
table; `20260901113726_DropSchemaMarkers` drops it, with a hand-written `Down` reproducing F-01's
original shape. Both directions were written by hand because EF generates nothing — the entity left
the model in F-02, so EF believed the table was already absent. This closes the deferred-destructive
handoff and is the first destructive migration this project has run.

**Delivery is at-least-once, deliberately.** A crash between "the provider accepted the message" and
"the row is marked `Sent`" resends on the next lease expiry. Duplicating a cancellation email is
acceptable; losing one violates the milestone's guardrail.

### Gotchas confirmed or discovered in phases 2-4

- **`dotnet ef` with `--no-build` silently uses a stale Debug assembly.** A migration added in the
  same session is reported as "not found" until a real build runs. Bit this change twice.
- **Angular 22's `@angular/build:application` builder has no `ngswConfigPath` option.** The service
  worker is enabled by setting `"serviceWorker": "ngsw-config.json"` — the config *path*, not a
  boolean. The older browser-builder syntax fails schema validation outright.
- **`ngsw-config.json` declares empty `assetGroups`/`dataGroups` on purpose.** The service worker
  exists for push only; the PRD locks "no offline-first guarantee", and caching a live schedule
  would seed stale-data bugs into S-03/S-04.
- **PWA icons are placeholders** — flat `#1f2937` squares generated at 192px and 512px (plus a
  maskable variant). They are a visible rough edge on an installed app and want a real design pass.

### Follow-ups opened by phases 2-4

- **Real PWA icons.** Placeholders ship today.
- **Heartbeat volume.** One log line every 15s is ~5,760 lines/day. Readable in `az webapp log tail`,
  but worth revisiting if log noise becomes a problem — either a longer interval or logging only
  when counts are non-zero.

## Test data (Staging only) — S-24, 2026-09-22

The single App Service holds the environment used for manual testing. Change
`test-environment-seed-data` added a config-gated `TestDataSeeder`
(`src/Infrastructure/TestData/`). It fills the database with a deterministic club: 200 members
(40 of them accountless, about half of those with a live claim code), 2 admins, 2 trainers, passes,
four weeks of classes back and four ahead with bookings, an exercise library and 8 active plans.

**It runs only in `Development` and `Staging`**, whatever the flags say. That environment check is
the only thing that protects this database on the day it becomes production.

### App Service settings

| Setting | Value | Note |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Staging` | Unset means `Production`, where the seeder refuses. `Staging` rather than `Test`/`Testing`, because `Testing` maps the authorization probe endpoints. The code branches only on `Development` and `Testing`, so `Staging` otherwise behaves exactly like `Production`. |
| `TestDataSeed__Enabled` | `true` | Seeds once, guarded on the `admin1@example.test` account. |
| `TestDataSeed__Password` | a secret | The one password every seeded account shares. **Never in the repo.** |
| `TestDataSeed__Reset` | `false` (normally) | `true` wipes every member, class, booking, pass, exercise, plan, outbox row and push subscription, keeping only the `AdminSeed__Email` account and the roles. The reset then reseeds. |

```
az webapp config appsettings set -n po-prostu-silka -g pps-rg --settings \
  ASPNETCORE_ENVIRONMENT=Staging TestDataSeed__Enabled=true TestDataSeed__Password='<secret>'
```

On a database that already holds domain data (anything beyond the AdminSeed account's member),
the first seed also needs `TestDataSeed__Reset=true`. Without it the seeder refuses, because the seed
would collide with existing names and e-mails, or overlap existing classes.

### Reseed procedure

**Container logging must be on**, or none of the log lines below are visible. App stdout is not
persisted by default, and `az webapp log tail` joins too late to see a startup. It was switched on
during the first seed (2026-09-22) with
`az webapp log config -n po-prostu-silka -g pps-rg --docker-container-logging filesystem`. Read a past
startup from `https://po-prostu-silka.scm.azurewebsites.net/api/logs/docker`, the
`*_default_docker.log` file.

First seed, 2026-09-22 09:09 UTC: `Seeded test data: 164 accounts, 204 members, 266 passes,
192 classes, 1009 bookings, 27 exercises, 10 plans.` The reset and the seed took about 16 s of cold
start on B1.

1. `az webapp config appsettings set -n po-prostu-silka -g pps-rg --settings TestDataSeed__Reset=true`.
   Changing an app setting restarts the app.
2. Wait until `GET /health` answers again, then open the newest `*_default_docker.log` under
   `https://po-prostu-silka.scm.azurewebsites.net/api/logs/docker`. Check for `Test data reset: ...`
   followed by `Seeded test data: ... accounts, ... members, ...`. Do not rely on `az webapp log tail`:
   it joins after the startup lines have been written.
3. `az webapp config appsettings set -n po-prostu-silka -g pps-rg --settings TestDataSeed__Reset=false`.
   This restarts the app again, and the log shows `Test data already present; seeding skipped.`

Seeded logins, all with the shared password:

- `admin1@example.test` (Admin, teaches nothing) and `admin2@example.test` (Admin + Trainer since
  S-25, instructs part of the schedule)
- `trener1@example.test` and `trener2@example.test` (Trainer)
- `czlonek001@example.test` … `czlonek160@example.test` (members; a few of them are blocked)

The `example.test` domain is reserved, so a stray e-mail can never reach a real person.

### Warnings

- **`Reset` left `true` wipes the database on every recycle.** App Service recycles without warning,
  and Always On restarts the app on its own schedule. Step 3 is not optional.
- **Before real members are entered, set `ASPNETCORE_ENVIRONMENT=Production`, or delete the setting.**
  Do this *first* when this environment goes live, and remove the three `TestDataSeed__*` settings in the
  same step. Only the environment makes the seeder refuse. A `TestDataSeed__Reset=true` still set
  on a `Staging` app would delete every real member.
- The data is generated relative to the day of the seed: passes and classes move with "today". A
  seed from several weeks ago has its whole window in the past, and a reseed renews it.

## Staff holding member data (S-25) — 2026-09-22

Since S-25 a trainer or an admin holds no karnet, no booking and no training plan: the API refuses to
create them (`member_is_staff`). Rows that **predate** the rule stay where they are. Their holder can
no longer see them, and nothing migrates them, because a clean-up migration would have no working
`Down`. This read-only query lists the people concerned. Run it against the database in the Azure
portal's query editor or in `sqlcmd`:

```sql
-- Members whose account holds Trainer or Admin AND who still hold live member data.
SELECT m.Id, m.DisplayName, m.Email,
       CASE WHEN EXISTS (SELECT 1 FROM MembershipPasses p
                         WHERE p.MemberId = m.Id
                           AND p.ValidTo >= CAST(SYSUTCDATETIME() AS date)) THEN 1 ELSE 0 END AS HasLivePass,
       CASE WHEN EXISTS (SELECT 1 FROM Bookings b JOIN Classes c ON c.Id = b.ClassId
                         WHERE b.MemberId = m.Id AND b.Status = 0      -- BookingStatus.Active
                           AND c.StartsAt > SYSUTCDATETIME()) THEN 1 ELSE 0 END AS HasFutureBooking,
       CASE WHEN EXISTS (SELECT 1 FROM TrainingPlans t
                         WHERE t.MemberId = m.Id AND t.Status = 0      -- TrainingPlanStatus.Active
                        ) THEN 1 ELSE 0 END AS HasActivePlan
FROM Members m
WHERE m.UserId IS NOT NULL
  AND EXISTS (SELECT 1 FROM AspNetUserRoles ur JOIN AspNetRoles r ON r.Id = ur.RoleId
              WHERE ur.UserId = m.UserId AND r.NormalizedName IN ('TRAINER', 'ADMIN'))
  AND (   EXISTS (SELECT 1 FROM MembershipPasses p
                  WHERE p.MemberId = m.Id AND p.ValidTo >= CAST(SYSUTCDATETIME() AS date))
       OR EXISTS (SELECT 1 FROM Bookings b JOIN Classes c ON c.Id = b.ClassId
                  WHERE b.MemberId = m.Id AND b.Status = 0 AND c.StartsAt > SYSUTCDATETIME())
       OR EXISTS (SELECT 1 FROM TrainingPlans t WHERE t.MemberId = m.Id AND t.Status = 0));
```

Clean-up is manual and goes through the admin UI, not SQL. Release a booking from the class's
bookings overlay: it holds a spot a real member could take. Shorten or revoke a karnet on
`/admin/members/<id>/passes`, which stays reachable by URL for a staff member. The app has no
route that ends a plan, so an active plan held by staff stays in place. That is harmless, because
`/api/plans/mine` refuses its holder. The Staging seed never gives staff any of the three, so the
query returns nothing there after a reseed.

## Notification delivery hardening — 2026-09-25

### What happened

On 2026-09-24 push notifications stopped arriving on Staging, and email with them. The logs showed
the outbox worker taking **about an hour per pass** from 16:00 UTC (heartbeats at 17:04, 18:06,
19:12), with 114 messages pending. Nothing was ever marked `Failed`, so `/health` answered `Healthy`
the whole time. It was found only because someone was waiting for a push.

The cause was a chain of three things:

1. Every cancellation emailed the test-data club, whose members all live on `example.test`
   (S-24), which spent the managed domain's send quota on mail that could not arrive.
2. When ACS answered 429, its SDK **retried on its own and honoured `Retry-After`**, silently,
   inside a single send call. That parked the worker's only loop.
3. Push and email shared one queue ordered by due time, so every push waited behind the emails.

### Code changes (commits `9ed906f` and the one after it)

| Change | Effect |
| --- | --- |
| One lane per channel, push first | A slow or throttled email can no longer delay a push |
| `Outbox:PushMaxAge` = 1 h | An older push is dropped (`push_expired`), never sent late in a burst |
| ACS client: retries off, 30 s network timeout | A 429 reaches the outbox at once instead of blocking the pass |
| New outcome `Throttled` | A 429 waits out `Retry-After` (clamped to 15 s–1 h) **without spending an attempt**, and the rest of the email batch waits with it |
| A transient or throttled email ends the email lane for that pass | ACS is asked once per window, not once per message |
| Reserved domains are never sent to | `.test`, `.example`, `.invalid`, `.localhost` and `example.com/net/org` are dropped (`reserved_domain`) |
| `/health` checks two more things | `Degraded` when the oldest undelivered message is older than `Outbox:MaxUndeliveredAge` (30 min), or the worker has not finished a pass for `Outbox:WorkerStallAfter` (5 min) |

`/health` stays `Degraded` rather than `Unhealthy`, and **`Degraded` still answers 200.** Any alert
must match the body text `Healthy`, not the status code. The same applies to App Service's built-in
Health check: it only reacts to non-2xx, so it will never see a delivery problem.

### Before production: alert on `/health` (required)

**Done in S-28 (2026-09-25)**, see "Client-ready environment (S-28)" below for the commands as run.

Without this, the new checks only help whoever happens to open `/health`.

1. Create an Application Insights resource in `pps-rg` (workspace-based), if there is none yet.
2. Application Insights → **Availability** → **Add Standard test**:
   - URL `https://<app>.azurewebsites.net/health`, test frequency 5 minutes, 3 or more locations.
   - **Content match**: `Healthy`, "content must contain". This is what catches `Degraded`.
   - Keep the default alert (it fires when 2 or more locations fail).
3. Open the test's alert rule and attach an **action group** that emails the people who can act.
4. Verify the path end to end once. On Staging, temporarily set `Outbox__WorkerStallAfter` to
   `00:00:01`. A pass runs every 15 s, so `/health` is then `Degraded` almost all the time. Wait for
   the alert email, then remove the setting (both changes restart the app).

### Before production: ACS sender domain and quota (required)

The Azure Managed Domain (`*.azurecomm.net`, see "The managed-domain decision" above) has low
default send limits. In production, one cancellation sends one email per booked member. A few
cancellations close together can exceed the limit. The outbox now survives that without losing mail
(throttling costs no attempts), but mail arrives late and `/health` goes `Degraded`.

1. Pick the sending domain (e.g. a subdomain such as `mail.<club-domain>`), with DNS access to it.
2. Email Communication Service → **Provision domains** → **Add domain** → Custom domain.
3. Add the TXT (ownership) record it shows, wait for **Verified**, then add the SPF and both DKIM
   CNAME records and wait for all three to verify.
4. **Connect** the domain to the Communication Service, and create a sender username (e.g.
   `powiadomienia`).
5. Set `Acs__SenderAddress` to `powiadomienia@mail.<club-domain>`. No code change is needed.
6. Check the current limits for a custom domain in the ACS documentation (they change, so none
   are recorded here). If they are below the club's worst case (largest class × cancellations per
   hour), request a quota increase through an Azure support request for Communication Services.

### Before production: a separate environment (strongly recommended)

Today there is one environment, and `main` deploys straight to it. A stall like this one would
have reached members directly.

- A second App Service and Azure SQL database for production, and the current one stays as Staging.
- Production settings: `ASPNETCORE_ENVIRONMENT=Production`, and **no** `TestDataSeed__*` settings
  (the seeder refuses outside Development/Staging anyway). Use its own `AdminSeed__*`, its own
  VAPID keypair (rotating keys later invalidates every subscription), and the custom-domain
  `Acs__SenderAddress`.
- CI: keep `main` → Staging. Promote the same artifact to production through a GitHub
  **environment** with required reviewers, after the Staging deploy is healthy.
- Point the availability test above at **both** environments.

## Client-ready environment (S-28) — 2026-09-25

Plan: `context/changes/client-ready-environment/plan.md`. Every command below is written with its
names spelled out, so a second environment is a re-run with other names (M-9 GL-01).

### Response hygiene (phase 1)

Live since commit `fba72cd`. `curl -sI https://po-prostu-silka.azurewebsites.net/` shows HSTS
(`max-age=31536000`), the enforced CSP, `X-Content-Type-Options`, `X-Frame-Options: DENY`,
`Referrer-Policy`, `Permissions-Policy`, and no `Server` header. The same set appears on static
files. **A new external origin in the SPA is a CSP change** (`src/Api/Http/SecurityHeaders.cs`),
and critical-CSS inlining must stay off in `angular.json`, because its `onload` is an inline script.

### `/health` semantics (phase 2)

- E-mail stuck past 30 min → `Degraded`, **unless** the lane is throttled: some undelivered e-mail
  carries `LastError = acs_429`. Then the threshold is `Outbox:ThrottledMaxUndeliveredAge` (3 h).
  Push is always judged against 30 min.
- `TestDataSeed__Reset=true` (with Enabled and a password, under Staging) → `Degraded` with
  "TestDataSeed:Reset is on: the next restart wipes the database". Turning Reset off clears it.

### Observability resources (phase 3)

Git Bash rewrites `/subscriptions/...` arguments into Windows paths. Export `MSYS_NO_PATHCONV=1`
before any command that passes a resource id.

```bash
export MSYS_NO_PATHCONV=1
SUB=1b1298d8-ca6a-4a57-a189-192ff31fbd3a; RG=pps-rg; APP=po-prostu-silka; OWNER=karol.rumianowski@gmail.com
RGID=/subscriptions/$SUB/resourceGroups/$RG

# Workspace with a 0.1 GB/day cap and 30-day retention (registers Microsoft.OperationalInsights on first use)
az monitor log-analytics workspace create -g $RG -n pps-logs -l polandcentral --retention-time 30 --quota 0.1

# Workspace-based Application Insights
az config set extension.use_dynamic_install=yes_without_prompt
az monitor app-insights component create --app pps-ai -g $RG -l polandcentral --kind web   --application-type web --workspace pps-logs

# Action group: e-mail to the owner
az monitor action-group create -g $RG -n pps-owner --short-name ppsowner   --action email owner $OWNER usecommonalertschema

# Standard availability test on /health. The content match is CASE-SENSITIVE on purpose:
# "Unhealthy" contains "healthy". Pass --locations once PER location, because a space-separated
# list silently keeps only the first. Do not pass --kind (it accepts only ping/multistep).
AI=$RGID/providers/microsoft.insights/components/pps-ai
az monitor app-insights web-test create -n pps-health -g $RG -l polandcentral   --defined-web-test-name pps-health --synthetic-monitor-id pps-health --web-test-kind standard   --enabled true --frequency 300 --timeout 30 --retry-enabled true   --locations Id=emea-nl-ams-azr --locations Id=emea-gb-db3-azr --locations Id=emea-fr-pra-edge   --locations Id=emea-se-sto-edge --locations Id=emea-ch-zrh-edge   --request-url https://$APP.azurewebsites.net/health --http-verb GET --expected-status-code 200   --content-validation content-match=Healthy ignore-case=false pass-if-text-found=true   --ssl-check true --tags "hidden-link:$AI=Resource"

# Its alert: 2+ locations failing -> pps-owner. The CLI has no flag for this criterion, so ARM:
# PUT $RGID/providers/Microsoft.Insights/metricAlerts/pps-health-availability?api-version=2018-03-01
# with criteria {"odata.type":"Microsoft.Azure.Monitor.WebtestLocationAvailabilityCriteria",
# "webTestId":<pps-health id>,"componentId":<pps-ai id>,"failedLocationCount":2}, scopes [test, ai],
# evaluationFrequency PT1M, windowSize PT5M, severity 1, autoMitigate true, actions [pps-owner].
# Body kept as run: az rest --method put --url ... --body @avail-alert.json

# Server errors: any exception or 5xx request in 5 minutes
az monitor scheduled-query create -n pps-server-errors -g $RG --scopes "$AI" --location polandcentral   --severity 1 --evaluation-frequency 5m --window-size 5m --auto-mitigate true   --condition "count 'ServerErrors' > 0"   --condition-query ServerErrors="union (exceptions | project timestamp), (requests | where toint(resultCode) >= 500 | project timestamp)"   --action-groups "$RGID/providers/microsoft.insights/actionGroups/pps-owner"

# Budget $25/month on the resource group, 80% actual + 100% forecast -> owner.
# `az consumption budget create-with-rg --notifications` rejects thresholdType, so ARM:
az rest --method put --url "https://management.azure.com$RGID/providers/Microsoft.Consumption/budgets/pps-monthly?api-version=2023-11-01"   --body '{"properties":{"category":"Cost","amount":25,"timeGrain":"Monthly",
    "timePeriod":{"startDate":"2026-09-01T00:00:00Z","endDate":"2028-09-30T00:00:00Z"},
    "notifications":{
      "actual80":{"enabled":true,"operator":"GreaterThanOrEqualTo","threshold":80,"thresholdType":"Actual","contactEmails":["'$OWNER'"],"locale":"pl-pl"},
      "forecast100":{"enabled":true,"operator":"GreaterThanOrEqualTo","threshold":100,"thresholdType":"Forecasted","contactEmails":["'$OWNER'"],"locale":"pl-pl"}}}}'

# Turn on the exporter (restarts the app)
az webapp config appsettings set -g $RG -n $APP --settings   "APPLICATIONINSIGHTS_CONNECTION_STRING=$(az monitor app-insights component show --app pps-ai -g $RG --query connectionString -o tsv)"
```

App setting added: `APPLICATIONINSIGHTS_CONNECTION_STRING`. The host registers the exporter only
when it is set (`src/Api/Telemetry/Telemetry.cs`). Right after, `requests` held only
request-parented dependencies, and none of the worker's polling.

**Gotcha:** `az monitor action-group test-notifications create` (and the REST `createNotifications`)
answered "There are no valid receivers in the request" for `pps-owner`. Use the portal's **Test
action group** instead.


### PR gate and rollback (phase 4)

- `.github/workflows/ci.yml` runs on every pull request to `main`: lint, SPA build and specs, the
  wwwroot staging step, `dotnet test`, and the idempotent migration script. It has one job, named
  **`checks`**. Branch protection requires that name, so renaming the job is a protection change too.
  It has no path filter and uses no secrets.
- `deploy.yml` ignores pushes that touch only `context/**` or `*.md` (`workflow_dispatch` still
  deploys on demand). It uploads `./publish` as `publish-<run id>`, kept 30 days.
- `rollback.yml`: **Actions → Rollback App Service to an earlier deploy → Run workflow**, with the
  `run_id` of the `deploy.yml` run to go back to (the number in that run's URL). It redeploys that
  artifact with the publish profile and **runs no migration**. `deploy.yml` and `rollback.yml`
  share the concurrency group `deploy-po-prostu-silka`, so they queue instead of interleaving.

Branch protection, as applied (needs `gh auth login` first):

```bash
gh api -X PUT repos/rumek/po-prostu-silka/branches/main/protection --input - <<'JSON'
{
  "required_status_checks": { "strict": true, "contexts": ["checks"] },
  "enforce_admins": true,
  "required_pull_request_reviews": { "required_approving_review_count": 0 },
  "restrictions": null,
  "allow_force_pushes": false,
  "allow_deletions": false
}
JSON
gh api repos/rumek/po-prostu-silka/branches/main/protection --jq '{checks: .required_status_checks.contexts, strict: .required_status_checks.strict, admins: .enforce_admins.enabled}'
```

From here on, "push to staging" means **merging a PR into `main`**. A direct `git push origin main`
is rejected.
