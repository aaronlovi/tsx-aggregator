# Plan: Score-13 Email Alert System

## Context
- Research: `.prompts/metrics-monitoring-alerts-research/research.md` (Q4: Email Alerts, Q2: Where to Compute Scores)
- Guidelines: `CLAUDE.md`
- Scope: **Email alerts only** — no Prometheus, no Grafana, no Docker additions

## Background

The user wants a BackgroundService in tsx-aggregator that periodically computes the score-13 company list, detects changes, and sends an email containing the previous list, new list, and diff (added/removed tickers).

### Key Findings from Research

- **Process**: tsx-aggregator (has access to `IStocksDataRequestsProcessor` for DB data and `IQuoteService` for prices)
- **Score computation**: Construct `CompanyFullDetailReport` objects from gRPC reply data, check `OverallScore == 13`. The constructor and score property are in `tsx-aggregator.models`, already referenced by tsx-aggregator.
- **Data path**: `StocksDataRequestsProcessor.ProcessGetStocksForExchangeRequest()` builds `GetStocksDataReplyItem` objects (price=0), then prices are filled from `QuoteService`. Items missing prices are removed (companies without prices can't have complete scores). See `StockDataService.cs:82-91`.
- **Email library**: MailKit (replacement for obsolete `SmtpClient`)
- **SMTP**: Gmail SMTP with app password (`smtp.gmail.com:587`, STARTTLS). SMTP password goes in User Secrets (project has `UserSecretsId` configured in csproj), not in `appsettings.json`
- **Previous list storage**: In-memory (on restart, first computation establishes baseline — no alert sent)
- **Toggle**: New `RunScore13AlertService` in `HostedServicesOptions` (existing pattern)
- **Config structure**: New `AlertSettings` section in `appsettings.json` for non-secret config (SMTP host, port, sender email, recipients list, check interval). SMTP password via User Secrets or environment variable `AlertSettings__SmtpPassword`

### Email Format

```
Subject: TSX Score-13 Alert: 2 added, 1 removed

Score-13 Companies Changed
==========================

New List (5 companies):
  - ABC.TO (ABC Corp)
  - DEF.TO (DEF Inc)
  - GHI.TO (GHI Ltd)
  - JKL.TO (JKL Holdings)
  - MNO.TO (MNO Group)

Previous List (4 companies):
  - ABC.TO (ABC Corp)
  - DEF.TO (DEF Inc)
  - PQR.TO (PQR Industries)
  - MNO.TO (MNO Group)

Added (2):
  + GHI.TO (GHI Ltd)
  + JKL.TO (JKL Holdings)

Removed (1):
  - PQR.TO (PQR Industries)
```

## Instructions
1. Read research.md (Q2 and Q4 sections)
2. Design implementation as checkpoints
3. Each checkpoint must include:
   - Build: what to implement
   - Test: what unit tests to write for THIS checkpoint's code
   - Verify: how to confirm all existing + new tests pass before moving on
4. NEVER design a dedicated "testing" checkpoint at the end. Tests are written alongside the code they verify, within the same checkpoint. Each checkpoint must leave the test suite green.

## Key Files to Reference

| File | Why |
|------|-----|
| `src/tsx-aggregator/Program.cs` | Service registration, HostedService toggle pattern (lines 83-99), Kestrel HTTP/2 config |
| `src/tsx-aggregator/Services/StockDataService.cs` | `GetStocksData()` — data path to replicate: call `StocksDataRequestsProcessor`, fill prices from `QuoteService`, remove items missing prices (lines 60-94) |
| `src/tsx-aggregator/Services/StocksDataRequestsProcessor.cs` | `ProcessGetStocksForExchangeRequest()` — builds `GetStocksDataReplyItem` from DB |
| `src/tsx-aggregator/QuotesService/QuoteService.cs` | `IQuoteService` interface, `PostRequest()` pattern, `QuoteServiceReady` gate |
| `src/tsx-aggregator.models/AggregatorData/CompanyFullDetailReport.cs` | Constructor (16 params), `OverallScore` computed property (lines 155-168), `DoesPassCheck_Overall` |
| `src/tsx-aggregator.models/HostedServicesOptions.cs` | Existing toggle pattern — add `RunScore13AlertService` |
| `src/tsx-aggregator/appsettings.json` | Add `HostedServices.RunScore13AlertService`, new `AlertSettings` section |
| `src/Directory.Packages.props` | Add `MailKit` package version |
| `src/stock-market-webapi/Controllers/CompaniesController.cs` | `GetDashboardAggregates()` (line 330) — reference for how `CompanyFullDetailReport` is constructed from gRPC reply |

## Constraints

- All usings must be explicit (no implicit usings — `Directory.Build.props`)
- `TreatWarningsAsErrors: true` — no warnings allowed
- `Nullable: enable` — nullable reference types enforced
- Follow existing patterns for DI registration, BackgroundService lifecycle, and options configuration
- The BackgroundService must tolerate `QuoteService` not being ready yet (use the `QuoteServiceReady` gate or similar)
- MailKit replaces the obsolete `System.Net.Mail.SmtpClient`

## Output
Write plan to `.prompts/score13-email-alerts-plan/plan.md`:
- Ordered checkpoints (implementation + tests each — no checkpoint without tests unless it is purely non-code work like documentation or configuration)
- Files to create/modify per checkpoint
- Metadata block (Status, Dependencies, Open Questions, Assumptions)
