---
project: po-prostu-silka
researched_at: 2026-08-30
recommended_platform: Azure App Service (Linux, B1) + Azure SQL Database
runner_up: Railway
context_type: mvp
tech_stack:
  language: C# (API) + TypeScript (SPA)
  framework: ASP.NET Core Web API + Angular
  runtime: .NET (EF Core + SQL Server)
---

## Recommendation

**Deploy on Azure App Service (Linux, B1 tier) with Azure SQL Database (Basic DTU tier).**

The stack pins SQL Server via EF Core, and Azure is the only shortlisted platform with a managed SQL Server offering — every alternative requires either self-managing a SQL Server container (~$20–25/mo extra, degraded deploy semantics) or swapping the database to Postgres (a stack change outside this decision's scope). With Azure SQL Basic at ~$4.90/mo, Azure is simultaneously the cheapest way to keep the stack unchanged (~$18/mo all-in), satisfying the cost-minimization answer from the interview. It also matches `tech-stack.md`'s own `deployment_target: azure-app-service` hint, runs .NET natively (no Dockerfile needed), and supports the always-on background hosted services the notification flow requires (B1 + Always On enabled). The persistent-connections requirement eliminated serverless-only platforms; single-region deployment (Poland Central, GA, 3 availability zones) neutralized any edge-platform advantage.

## Platform Comparison

Hard filters applied before scoring:

- **Vercel, Netlify** — dropped: no .NET runtime, and serverless-only models cannot run always-on hosted services (interview: persistent connections required).
- **Cloudflare** — dropped despite user familiarity: Workers has no .NET runtime (JS/TS/Python/Rust only; .NET-on-WASM is community-experimental); Cloudflare Containers (GA 2026-04-13) is designed for on-demand, sleep-after-inactivity workloads, not 24/7 hosted services; no SQL Server path (D1 is SQLite; Hyperdrive is Postgres/MySQL only — checked 2026-08-30). Cloudflare remains a fine future home for the Angular SPA as a static asset host, but note Pages shows soft-deprecation signals in favor of Workers static assets (checked 2026-08-30).

Scoring of the survivors (Pass / Partial / Fail per criterion; all statuses checked 2026-08-30):

| Platform | CLI-first | Managed/Serverless | Agent-readable docs | Stable deploy API | MCP / Integration | Total |
|---|---|---|---|---|---|---|
| Azure App Service | Pass | Pass | Pass | Pass | Pass | 5 Pass |
| Railway | Pass | Pass | Pass | Pass | Pass | 5 Pass* |
| Render | Pass | Pass | Pass | Pass | Pass | 5 Pass* |
| Fly.io | Pass | Partial | Pass | Partial | Partial | 2 Pass, 3 Partial |

\* Railway and Render tie Azure on the five criteria but fail the stack-fit tiebreaker: neither offers managed SQL Server, so keeping the pinned stack costs ~$25–32/mo (DIY SQL Server container) vs Azure's ~$18/mo. The five-criteria tie was broken by the tech-stack hard constraint and the cost interview answer, per the criteria's own weighting guidance.

**Azure App Service** — .NET is first-class (GA) on Linux; `az` CLI covers deploy/logs/config end-to-end; Microsoft Learn docs are markdown in the MicrosoftDocs/azure-docs GitHub repo (no llms.txt); zip-deploy via `az webapp deploy` is deterministic; Azure MCP Server 1.0 is stable (GA since Nov 2025). Caveat: deployment slots (instant rollback) require Standard S1+ — on B1, rollback = redeploy previous artifact. Windows-hosted .NET currently receives delayed runtime patches (App Service team blog, Oct 2025) — Linux is unaffected and is the chosen OS.

**Railway** — best pure agent ergonomics of the pool: every docs page has a `.md` twin plus `llms.txt`/`llms-full.txt`; official MCP server bundled into the CLI (`railway mcp`, GA); containers always-on by default; EU West Metal (Amsterdam) region GA. But Railpack does not support .NET (Dockerfile required, per official ASP.NET Core guide), and SQL Server exists only as a community template holding ~2GB RAM (~$20/mo at $10/GB/mo). ~$5–8/mo only after a Postgres swap.

**Render** — CLI GA (v2.25), rollback via CLI/REST API, docs serve markdown + `llms.txt`, hosted MCP server GA since Aug 2025, Frankfurt region GA, free static-site hosting for the Angular SPA. But .NET is Docker-only, free web services spin down after 15 idle minutes (kills hosted services — Starter $7/mo is the floor), free Postgres expires after 30 days, and SQL Server needs a $25/mo private service that loses zero-downtime deploys.

**Fly.io** — cheapest always-on compute (shared-cpu-1x 512MB ≈ $3.19/mo; free tier removed for new accounts since Oct 2024), real VMs so persistent processes just work. But no first-class rollback (redeploy by image digest), `auto_stop_machines` defaults would kill the notification workers unless disabled, the MCP server is experimental, and the database story is the weakest: Managed Postgres is region-limited (ams/fra/lhr; no Warsaw) at $38/mo minimum, legacy Fly Postgres is explicitly unmanaged, the Supabase partnership was deprecated 2025-04-11, and there is no SQL Server offering.

### Shortlisted Platforms

#### 1. Azure App Service (Recommended)

Only managed-SQL-Server home for the pinned stack; native .NET deployment with no Docker layer; matches the bootstrap-time deployment hint; cheapest total cost that keeps the stack unchanged (~$13.14/mo B1 Linux + ~$4.90/mo Azure SQL Basic); Poland Central region GA with 3 availability zones; Azure MCP Server stable; GitHub Actions integration is a documented one-command setup.

#### 2. Railway

Wins on agent-readable docs and CLI/MCP polish, always-on by default, transparent usage pricing. The gap: no managed SQL Server (community-template container adds ~$20/mo and redeploy downtime) and no .NET buildpack (Dockerfile tax). Becomes the top pick at ~$10–15/mo if the project ever swaps EF Core to the Postgres provider — that swap would warrant re-running `/10x-tech-stack-selector` first.

#### 3. Render

Same shape as Railway with free CDN-backed static hosting for the Angular SPA and a mature rollback API. The gap: identical SQL Server problem (worse — $25/mo floor with degraded deploy semantics), free-tier spin-down bars the notification workers, and free Postgres expires after 30 days. ~$13/mo with a Postgres swap.

## Anti-Bias Cross-Check: Azure App Service

### Devil's Advocate — Weaknesses

1. **No deployment slots on B1** — rollback is "redeploy the previous artifact": minutes of downtime after a bad deploy instead of an instant slot swap. Slots start at Standard S1 (~3× the price).
2. **App-pool recycles interrupt background workers mid-batch.** Azure restarts apps without warning during platform maintenance. A hosted service halfway through emailing booked members when it recycles silently drops the rest — a direct hit on the "no missed cancellations" guardrail unless notifications go through an outbox/retry pattern.
3. **Azure SQL free serverless tier auto-pauses and quota-caps.** A background poller keeps waking it, burning the 100k vCore-second monthly quota; when exhausted, the default is the database pauses until next month. Basic DTU (~$4.90/mo, 2GB cap) is the safe choice; the free tier is a trap for this workload.
4. **Bill-shock surface.** Application Insights ingestion, bandwidth, and forgotten resources can silently exceed compute cost. Azure is the easiest platform in this shortlist for a cost-minimizing solo dev to accidentally spend $40 instead of $15.
5. **CI auth plumbing is the classic time-sink.** OIDC federated credentials for GitHub Actions require Entra app registration and role scoping; the auto-generated workflow falls back to less-secure publish-profile secrets. Budget half a day.

### Pre-Mortem — How This Could Fail

The deploy worked on day one, which is why nobody read the fine print. The app went live on B1 with the free Azure SQL serverless database because "free beats $4.90." Mid-January, the notification poller's constant wake-ups exhausted the vCore-second quota and the database paused itself; members opened the app to a spinner, and the owner found out from an angry text. The dev flipped the DB to paid under pressure, misread the tiers, and picked vCore serverless — the bill jumped to $40/month. Meanwhile "Always On" had never been enabled (it's a checkbox, off by default even on B1), so for the first six weeks the hosted service only ran when someone happened to hit the site — cancellation emails arrived hours late, exactly the Excel-era failure the app existed to kill. Trust eroded; the owner kept the Excel sheet "as backup," which meant double bookkeeping. The final straw: a bad Friday deploy with no slots meant twenty minutes of downtime during peak evening booking, and rollback had never been rehearsed.

### Unknown Unknowns

- **Always On is off by default even on B1.** Buying the tier doesn't enable the setting; hosted services die after ~20 idle minutes until `az webapp config set --always-on true` is run.
- **`WEBSITE_RUN_FROM_PACKAGE` makes wwwroot read-only.** Any runtime file writes (uploads, local caches) fail post-deploy in a way local dev never shows.
- **Email sending is a project of its own.** Azure Communication Services email needs domain verification, DNS records, and sender approval — multi-day elapsed time that belongs in week 1 of the 3-week MVP, not week 3.
- **Windows vs Linux plan choice is effectively irreversible**, and Windows-hosted .NET currently gets delayed runtime patches (App Service team, Oct 2025). Create the plan as Linux.
- **Recycles demand idempotent notifications.** Because the platform restarts apps at will, the email+push-on-cancel flow needs an outbox table and retry, not a fire-and-forget loop — an architecture requirement disguised as an ops footnote.

## Operational Story

- **Preview deploys**: no PR preview environments at B1 (slots need Standard S1+). Practical MVP substitute: GitHub Actions runs build + tests on every PR; only merges to `main` deploy to the single production app (matches `ci_default_flow: auto-deploy-on-merge` from tech-stack.md).
- **Secrets**: connection strings and API keys live in App Service app settings (`az webapp config appsettings set`), injected as environment variables; CI credentials live in GitHub Actions secrets as OIDC federated credentials (no long-lived password). Rotation = update the app setting, app restarts automatically. Nothing secret is committed to the repo.
- **Rollback**: `dotnet publish` artifacts are retained per-release by the GitHub Actions run; rollback = re-run `az webapp deploy --src-path <previous>.zip` (or re-run the previous workflow's deploy job). Typical time-to-revert: 3–5 minutes including cold start. EF Core migrations do NOT roll back automatically — write reversible migrations and never couple a destructive migration to the same deploy as the code that stops needing it.
- **Approval**: agent may deploy to production unattended (auto-deploy-on-merge is the accepted flow), read logs, and update non-secret app settings. Human-only: deleting any Azure resource, changing pricing tiers, rotating the SQL admin password, and running destructive EF migrations against the production DB.
- **Logs**: `az webapp log tail --name <app> --resource-group <rg>` streams runtime logs read-only; `gh run list` / `gh run view` cover pipeline logs; Azure MCP Server 1.0 (GA) exposes structured queries over the same surface if CLI parsing becomes a recurring pattern.

## Risk Register

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| Hosted service idle-stops; notifications delayed hours | Unknown unknowns | H | H | Enable Always On on day one; add it to the deploy checklist and verify with a scheduled heartbeat log line |
| App recycle drops in-flight notification batch | Devil's advocate | M | H | Outbox table + idempotent retry for all email/push sends; never fire-and-forget |
| Free Azure SQL quota exhausts; DB pauses until next month | Devil's advocate | H (if free tier used) | H | Use Basic DTU (~$4.90/mo) from the start; free serverless tier explicitly rejected for this workload |
| Bad deploy with no slots = minutes of downtime | Devil's advocate | M | M | Rehearse artifact rollback once before launch; deploy outside peak booking hours |
| Bill creep from App Insights / forgotten resources | Devil's advocate | M | M | Single resource group; monthly budget alert at $25; cap App Insights sampling |
| ACS email domain verification blocks notification flow near deadline | Unknown unknowns | M | H | Start domain verification in week 1; fallback SMTP provider documented |
| Runtime file writes fail under `WEBSITE_RUN_FROM_PACKAGE` | Unknown unknowns | L | M | No runtime writes to wwwroot; anything written goes to Azure Blob Storage |
| Wrong OS at plan creation (Windows) → delayed .NET patches | Research finding | L | M | Create plan as Linux explicitly; verified before first deploy |
| EF migration not reversible during rollback | Pre-mortem | M | H | Reversible migrations policy; destructive column drops deferred one release behind the code change |
| CI auth setup (OIDC) burns a day mid-sprint | Devil's advocate | M | L | Timeboxed to setup day; publish-profile fallback acceptable for MVP if OIDC stalls |

## Getting Started

Validated against the stack (ASP.NET Core Web API + Angular, `dotnet` + `ng` toolchains, GitHub Actions) — commands checked against current `az` CLI docs 2026-08-30:

1. Install and log in: `winget install Microsoft.AzureCLI`, then `az login`.
2. Create the resource group and Linux plan in Poland Central: `az group create -n pps-rg -l polandcentral`, then `az appservice plan create -n pps-plan -g pps-rg --sku B1 --is-linux`.
3. Create the web app on the .NET runtime and enable Always On immediately: `az webapp create -n po-prostu-silka -g pps-rg -p pps-plan --runtime "DOTNETCORE:8.0"`, then `az webapp config set -n po-prostu-silka -g pps-rg --always-on true`.
4. Create Azure SQL (Basic tier, not the free serverless offer): `az sql server create` + `az sql db create --service-objective Basic`, then set the connection string via `az webapp config connection-string set`.
5. Wire GitHub Actions auto-deploy-on-merge: `az webapp deployment github-actions add --repo <owner>/PoProstuSilka -g pps-rg -n po-prostu-silka -b main --login-with-github`. Serve the built Angular SPA from the API's wwwroot (single app on B1; `ng build` output copied in the publish step) — Azure Static Web Apps split is a post-MVP option.

## Out of Scope

The following were not evaluated in this research:
- Docker image configuration
- CI/CD pipeline setup
- Production-scale architecture (multi-region, HA, DR)
