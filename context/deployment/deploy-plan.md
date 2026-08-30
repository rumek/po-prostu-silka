---
project: po-prostu-silka
platform: Azure App Service (Linux, B1)
status: blocked
last_updated: 2026-08-30
---

## What this is

The audit trail for the first deployment of po-prostu-silka, executed from the approved Plan Mode plan (`context/foundation/infrastructure.md` is the underlying platform decision). This records what was supposed to happen and what actually happened, so a future session — or a live run gone sideways — has ground truth instead of having to reconstruct it from chat history.

## Scope of this deployment

Deploy the ASP.NET Core API and the Angular SPA (as a static bundle served from the API's `wwwroot`) to a single Azure App Service (Linux, B1). Azure SQL Database is deliberately **not** provisioned in this deployment — there is no EF Core/connection string in the app yet, so a database would sit unused. It's planned for the deploy that introduces Identity/EF Core.

## Status: blocked on Azure subscription permissions

Steps A and B below are done and pushed. Step C (Azure resource creation) is blocked — see "Current blocker".

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

### C. Azure resources — BLOCKED

Planned commands (not yet run for `create`/`config` — `list-runtimes` succeeded):

```
az webapp list-runtimes --os-type linux   # ✅ ran — confirmed "DOTNETCORE:10.0" is available
az group create -n pps-rg -l polandcentral                        # ❌ blocked, see below
az appservice plan create -n pps-plan -g pps-rg --sku B1 --is-linux
az webapp create -n po-prostu-silka -g pps-rg -p pps-plan --runtime "DOTNETCORE:10.0"
az webapp config set -n po-prostu-silka -g pps-rg --always-on true
az webapp update -n po-prostu-silka -g pps-rg --https-only true
```

**Current blocker**: the Azure CLI session is logged in as `kr@anbast.com` against the **BizSpark** subscription (`d11bb64e-e45c-4c99-afd5-9629cb3ce6b8`, tenant `8e016995-c626-446c-9c57-c8369deb82d2`) — the only subscription visible to this account (`az account list` shows just the one). `az group create` fails with `AuthorizationFailed`: this identity has no `Microsoft.Resources/subscriptions/resourcegroups/write` permission on it.

**To unblock, one of**:
1. Get the `kr@anbast.com` identity granted a role with resource-group write access (e.g. Contributor) on the BizSpark subscription, scoped to a new resource group if the subscription owner prefers to limit blast radius, or
2. `az login` with a different account/subscription that does have write access (a personal Azure subscription, a free-tier subscription, etc.) and re-run from step C.

Nothing else in this plan can proceed until one of these happens — steps D (CI/CD secret + workflow trigger), E (live verification), and the rest of C all depend on the web app existing.

### D. CI/CD — partially done

- `.github/workflows/deploy.yml` is written and pushed — triggers on push to `main`, builds Angular static, stages it into `wwwroot`, runs `dotnet publish`, deploys via `azure/webapps-deploy@v3` using an `AZURE_WEBAPP_PUBLISH_PROFILE` secret.
- **Not yet done**: the secret itself. This needs the web app to exist first (`az webapp deployment list-publishing-profiles --xml`), then the user pastes it into the repo's `Settings → Secrets and variables → Actions`. Blocked by C.
- Auth approach is publish-profile, not OIDC: the local Azure CLI (`2.35.0`) predates federated-credential support, and GitHub CLI isn't installed to script around it. `infrastructure.md`'s risk register explicitly accepts this as an MVP fallback — revisit once the CLI is upgraded.

### E. Verification — not started (depends on C, D)

### F. This file — written now, mid-flight, specifically so the blocker and partial progress aren't lost between sessions. Update it once C unblocks and the remaining steps complete.

## Known follow-ups (not blocking, but don't lose track)

- Azure CLI locally is `2.35.0` (~2022) — very old. No OIDC support, missing newer command surface. Worth upgrading before the next infra-touching session.
- GitHub CLI (`gh`) is not installed — all GitHub-side operations (secrets, PR checks) went through the web UI / plain git. Installing it would remove a recurring manual step.
- `dotnet restore` on this project pulls from private organizational NuGet feeds (`anbast.pkgs.visualstudio.com`) configured in a machine-level NuGet.Config, in addition to nuget.org. Harmless now (zero private-feed packages in use), but if EF Core or other packages ever accidentally resolve from a private feed, GitHub Actions runners won't have access to it and CI will fail mysteriously — worth an explicit public-only `nuget.config` in the repo before that happens.
- Azure SQL Database is intentionally deferred to the deploy that introduces EF Core/Identity — don't forget to also enable Always On and HTTPS Only at that point if this deployment's step C.9/C.10 didn't already happen.
