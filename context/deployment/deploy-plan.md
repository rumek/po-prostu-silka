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

- Azure CLI locally is `2.35.0` (~2022) — very old. No OIDC support, missing newer command surface. Worth upgrading before the next infra-touching session.
- GitHub CLI (`gh`) is not installed — all GitHub-side operations (secrets, PR checks) went through the web UI / plain git. Installing it would remove a recurring manual step.
- `dotnet restore` on this project pulls from private organizational NuGet feeds (`anbast.pkgs.visualstudio.com`) configured in a machine-level NuGet.Config, in addition to nuget.org. Harmless now (zero private-feed packages in use), but if EF Core or other packages ever accidentally resolve from a private feed, GitHub Actions runners won't have access to it and CI will fail mysteriously — worth an explicit public-only `nuget.config` in the repo before that happens.
- Azure SQL Database is intentionally deferred to the deploy that introduces EF Core/Identity — don't forget to also enable Always On and HTTPS Only at that point if this deployment's step C.9/C.10 didn't already happen.
