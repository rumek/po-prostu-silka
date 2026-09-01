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
- Verified locally end-to-end: `dotnet publish` output is clean (no leaked Angular source — the csproj already carries `<Compile/Content/EmbeddedResource/None Remove="app\**" />` excludes), and a local run of the published app returned 200 on `/`, `/weatherforecast`, and an arbitrary unmapped route (SPA fallback).

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
- VAPID keys are a P-256 keypair, base64url-encoded raw (87-char public point, 43-char private
  scalar). Generated with `openssl ecparam -genkey -name prime256v1`; the local Python had no
  `cryptography` module.
- **Rotating the VAPID keypair invalidates every stored push subscription.** Members would silently
  stop receiving push until they re-subscribe. Treat it as a one-time value.

### Follow-up opened by this change

- **Custom sender domain.** Deferred deliberately (see above). Requires DNS access to the chosen
  domain and multi-day provider-side verification; closing it is a change to
  `Acs__SenderAddress` only.
