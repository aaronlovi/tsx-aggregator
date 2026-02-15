# Research: Metrics Export, Monitoring Dashboard & Score-13 Email Alerts

## Objective
Determine the best approach to:
1. Export application metrics from the .NET backend (score-13 count, instrument stats, processing timestamps)
2. Visualize those metrics in a Docker-hosted monitoring dashboard (Grafana or similar) — **no direct SQL queries from the dashboard tool**
3. Send email alerts when the score-13 company list changes, including the previous list, new list, and diff (added/removed tickers)

## Context
- Guidelines: `CLAUDE.md`
- Two-process backend: `tsx-aggregator` (worker + gRPC) and `stock-market-webapi` (REST API)
- Score computation: `OverallScore` is a computed property on `CompanyFullDetailReport` (sum of 13 boolean checks), NOT stored in the database
- Score-13 data is computed in `CompaniesController.GetDashboardAggregates()` (`src/stock-market-webapi/Controllers/CompaniesController.cs:330`) — it constructs `CompanyFullDetailReport` objects from gRPC reply data, then groups by `OverallScore`
- `PricePerShare` comes from in-memory `QuoteService` (Google Sheets), filled in by `StockDataService` (`src/tsx-aggregator/Services/StockDataService.cs`)
- Background services registered in `src/tsx-aggregator/Program.cs:86-99`, toggled via `HostedServices` in `appsettings.json`
- Docker stack: `docker-scripts/docker-compose.yml` (PostgreSQL 17.4 on port 5433, pgAdmin on port 8081, `tsx-data-network`)
- Existing package management: Central Package Management (`Directory.Packages.props`), .NET 10.0

## Questions to Answer

### 1. What is the best .NET metrics library for this use case?
- Compare `prometheus-net`, OpenTelemetry (`System.Diagnostics.Metrics` + OTLP exporter), and any other relevant options for .NET 10
- Evaluate: ease of integration, maturity, community support, Docker ecosystem compatibility
- Determine which library best supports exposing both numeric gauges (score-13 count, instrument counts) AND the constraint that the dashboard must NOT query the database directly
- Does .NET 10 have built-in metrics support that simplifies this (e.g., `System.Diagnostics.Metrics` with a Prometheus exporter)?

### 2. Where in the existing code should metrics be computed and updated?
- `CompaniesController.GetDashboardAggregates()` already constructs all `CompanyFullDetailReport` objects and groups by `OverallScore` — but this runs on-demand per HTTP request in the **web API process**, not the **tsx-aggregator worker process**
- `StocksDataRequestsProcessor.ProcessGetStocksForExchangeRequest()` (`src/tsx-aggregator/Services/StocksDataRequestsProcessor.cs:77`) builds `GetStocksDataReplyItem` objects from the DB but does NOT compute scores (it sets `PerSharePrice = 0M`)
- `StockDataService` (`src/tsx-aggregator/Services/StockDataService.cs`) fills in `PerSharePrice` from `QuoteService` before returning the gRPC reply
- Scores require both DB data AND live price data. Identify which process (tsx-aggregator vs stock-market-webapi) is the right place to periodically compute and export metrics
- If the tsx-aggregator worker is chosen: how would it access the same `CompanyFullDetailReport` score computation that currently lives in the web API layer? Would it need to call its own gRPC endpoint, or can the score computation be extracted into a shared service?
- If the web API is chosen: it currently only computes scores on-demand. Would a `BackgroundService` in the web API be appropriate, or does it conflict with the existing architecture where background work lives in tsx-aggregator?

### 3. Can Prometheus (or the chosen metrics tool) + Grafana surface a list of ticker symbol strings?
- Prometheus metrics are numeric time-series. Can a list of ticker symbols (e.g., "ABC.TO, DEF.TO, GHI.TO") be meaningfully represented?
- Approaches to evaluate: (a) Prometheus info metric / label-based gauge with ticker as label, (b) Grafana annotation from a webhook, (c) exposing the list via a separate REST endpoint that Grafana reads
- **Critical constraint**: the dashboard must NOT query the database directly. If the ticker list can only come from a REST endpoint, does Grafana support JSON API datasources without plugins, or is a plugin required?
- Provide a clear YES/NO conclusion: can the full ticker list be surfaced in Grafana without direct DB access?

### 4. How should email alerts with score-13 diff be implemented?
- The email must contain: previous score-13 ticker list, new score-13 ticker list, and the diff (added/removed tickers)
- **User's preference hierarchy**: If Prometheus+Grafana can handle the full alert (including ticker lists and diffs), use that. If not, the .NET application should handle email alerts independently — no Grafana involvement for alerts in that case.
- **Option A (Grafana alerting)**: Can Grafana alerts include arbitrary text (ticker lists, diffs) in the email body? Grafana alerts are typically threshold-based on numeric metrics. Evaluate whether Grafana notification templates can include the ticker list and diff, or whether this is impractical
- **Option B (.NET email service)**: The .NET application detects changes to the score-13 list, computes the diff, and sends emails directly via SMTP. Evaluate:
  - Where to store the previous score-13 list for comparison (in-memory, database table, file?)
  - Where to store the recipient email address(es) — likely just a simple config setting in `appsettings.json` or environment variable, not a full subscription management system
  - Which .NET email library to use (built-in `SmtpClient` is obsolete — evaluate MailKit or other recommended alternatives for .NET 10)
  - SMTP provider options for self-hosted Docker (local SMTP relay container vs external free SMTP service like Gmail SMTP with app password)
- **Option C (hybrid)**: Numeric metrics go to Prometheus/Grafana for dashboards; email alerts with ticker diffs are handled by the .NET app independently
- Recommend the best option with justification

### 5. What Docker services are needed and how do they integrate?
- For the chosen metrics approach: what Docker images are needed (Prometheus, Grafana, SMTP relay, etc.)?
- How does Prometheus discover and scrape the .NET app's metrics endpoint? Is the .NET app running inside Docker or on the host? (Currently the .NET apps run on the host, not in Docker — only PostgreSQL and pgAdmin are in Docker)
- If the .NET app runs on the host: how does Prometheus (in Docker) scrape `host.docker.internal` or equivalent on Windows?
- Configuration approach: file-provisioned Grafana datasource + dashboard (for version control), Prometheus `prometheus.yml` scrape config
- Port allocation: existing ports are 5433 (Postgres), 8081 (pgAdmin), 7001 (gRPC), 5000/5001 (REST API). Identify available ports for Prometheus (typically 9090) and Grafana (typically 3000)

### 6. What metrics should be exported?
- Based on the existing `GetDashboardStats` and `GetDashboardAggregates` endpoints, determine which metrics are valuable as Prometheus gauges:
  - Score-13 company count (and possibly counts for other score brackets)
  - Active instrument count, obsoleted instrument count
  - Instruments with/without processed reports
  - Unprocessed event count
  - Last raw ingestion timestamp, last aggregation timestamp
  - Score distribution (count per score bracket)
- For each metric, identify the source: which service/method already computes it, and how frequently it should be updated
- Are there any metrics that would require new database queries vs. metrics that can be derived from existing in-memory state?

## Explore
- `src/tsx-aggregator/Program.cs` — Service registration, hosted service pattern, DI container setup
- `src/tsx-aggregator/Services/StockDataService.cs` — How gRPC replies are built, where `PerSharePrice` is filled in
- `src/tsx-aggregator/Services/StocksDataRequestsProcessor.cs` — Request processing, `report_json` parsing
- `src/stock-market-webapi/Controllers/CompaniesController.cs` — `GetDashboardAggregates()` (line 330) and `GetDashboardStats()` (line 295) — where scores and stats are computed
- `src/stock-market-webapi/Program.cs` — Web API service registration, middleware pipeline
- `src/tsx-aggregator.models/AggregatorData/CompanyFullDetailReport.cs` — `OverallScore` computed property, 13 boolean checks
- `src/tsx-aggregator.models/HostedServicesOptions.cs` — Pattern for toggling background services
- `src/tsx-aggregator/QuotesService/QuoteService.cs` — How price data is fetched and cached
- `docker-scripts/docker-compose.yml` and `docker-scripts/.env` — Current Docker stack
- `src/Directory.Packages.props` — Current NuGet package versions (to check for existing metrics/telemetry packages)

## Output
Write findings to `.prompts/metrics-monitoring-alerts-research/research.md`:
- Answers to each of the 6 questions above
- Clear recommendation for metrics library (with justification)
- Clear YES/NO on whether Prometheus+Grafana can surface the ticker list without direct DB access
- Clear recommendation for email alert approach (Option A, B, or C) with justification
- Architecture diagram (text-based) showing the data flow: .NET app → metrics endpoint → Prometheus → Grafana, and separately the email alert flow
- Risks or concerns (performance impact of periodic score computation, SMTP reliability, metric cardinality)
- Metadata block (append at end):
  ## Metadata
  ### Status
  [success | partial | failed]
  ### Dependencies
  - [files or decisions this relies on, or "None"]
  ### Open Questions
  - [unresolved issues, or "None"]
  ### Assumptions
  - [what was assumed, or "None"]
