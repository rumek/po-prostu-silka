---
starter_id: dotnet
package_manager: dotnet
project_name: po-prostu-silka
hints:
  language_family: dotnet
  team_size: solo
  deployment_target: azure-app-service
  ci_provider: github-actions
  ci_default_flow: auto-deploy-on-merge
  bootstrapper_confidence: verified
  path_taken: custom
  quality_override: false
  self_check_answers:
    typed: true
    from_official_starter: true
    conventions: true
    docs_current: true
    can_judge_agent: true
  has_auth: true
  has_payments: false
  has_realtime: false
  has_ai: false
  has_background_jobs: true
---

## Why this stack

A solo builder shipping a gym booking-and-training-plans web app in 3 weeks chose the custom path to pin the stack they can judge: ASP.NET Core Web API (C#, EF Core with SQL Server) plus an Angular SPA as two sibling projects in one repository. Both halves clear all four agent-friendly gates — typed end-to-end (C# + TypeScript), convention-based official templates (`dotnet new webapi`, `ng new`), popular within their families, with current versioned docs. Auth (email+password, admin approval, block/unblock) maps to ASP.NET Core Identity; email + push notification delivery on class cancel/change maps to .NET hosted background services; the no-overbooking guardrail leans on SQL Server transactions via EF Core. Deployment targets Azure App Service (the starter default, and the natural home for ASP.NET Core + Azure SQL), with CI on GitHub Actions auto-deploying on merge. The five-point self-check came back clean, so no quality override is recorded; the accepted trade-off is two codebases of surface for a 3-week solo MVP, taken because .NET + Angular is where the builder is productive.
