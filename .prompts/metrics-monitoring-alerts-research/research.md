# Research: Metrics Export, Monitoring Dashboard & Score-13 Email Alerts

## Q1: Best .NET Metrics Library

### Options Compared

| Library | Version | Approach | NuGet Packages |
|---------|---------|----------|----------------|
| **prometheus-net** | 8.2.1 | Dedicated Prometheus client | `prometheus-net`, `prometheus-net.AspNetCore` |
| **OpenTelemetry** | 1.x | Vendor-neutral via `System.Diagnostics.Metrics` | `OpenTelemetry`, `OpenTelemetry.Exporter.Prometheus.AspNetCore` |
| **Prometheus.Client** | 6.1.0 | Fork of prometheus-net (2017), perf-focused | `Prometheus.Client`, `Prometheus.Client.AspNetCore` |

### Analysis

**prometheus-net** is the most popular dedicated Prometheus library for .NET (v8.2.1). It provides:
- Direct Prometheus metric types (Counter, Gauge, Histogram, Summary)
- ASP.NET Core middleware for `/metrics` endpoint
- Standalone `KestrelMetricServer` for hosting metrics on a separate port (useful for the tsx-aggregator which uses HTTP/2-only gRPC)
- Straightforward API: `Metrics.CreateGauge("name", "help", labelNames: new[] { "label1" })`
- gRPC instrumentation support

**OpenTelemetry** is the vendor-neutral standard. Microsoft officially documents the Prometheus+Grafana+OTel pattern for .NET. It uses `System.Diagnostics.Metrics` (built into .NET) with an OTel Prometheus exporter. More boilerplate but future-proof. Key packages: `OpenTelemetry.Exporter.Prometheus.AspNetCore`.

**Prometheus.Client** (v6.1.0) is a 2017 fork of prometheus-net focused on performance/minimal allocations. Less community adoption, fewer features.

### Recommendation: **prometheus-net**

Justification:
- Simplest integration for the use case (just Prometheus, no need for vendor-neutral OTLP)
- `KestrelMetricServer` solves the problem of serving HTTP/1.1 metrics from the tsx-aggregator process (which only serves HTTP/2 gRPC on port 7001)
- Well-documented label support for info metrics
- Most community examples and support
- Active development (v8.2.1)

NuGet packages needed:
- `prometheus-net` (core library, in tsx-aggregator)
- `prometheus-net.AspNetCore` (optional, if we want `/metrics` in the web API too)

---

## Q2: Where to Compute and Export Metrics

### Architecture Analysis

The score computation requires **both** DB data AND live price data:
- DB data: `processed_instrument_reports.report_json` (via `StocksDataRequestsProcessor`)
- Price data: `QuoteService` (in-memory cache from Google Sheets)
- Score computation: `CompanyFullDetailReport.OverallScore` (13 boolean checks, 5 require `PricePerShare`)

Current data flow for scores:
```
StocksDataRequestsProcessor.ProcessGetStocksForExchangeRequest()
  → builds GetStocksDataReplyItem (PerSharePrice = 0)
  → StockDataSvc.GetStocksData() fills prices from QuoteService
  → gRPC reply sent to web API
  → CompaniesController.GetDashboardAggregates() builds CompanyFullDetailReport objects
  → groups by OverallScore
```

### Process Selection

**tsx-aggregator is the right process** because:
- It has direct access to `IStocksDataRequestsProcessor` (DB data) and `IQuoteService` (prices)
- All existing BackgroundServices are in tsx-aggregator (consistent pattern)
- It already runs the gRPC server, so it has the complete data pipeline

**Challenge**: tsx-aggregator's Kestrel is HTTP/2 only (`Program.cs:52`: `options.Protocols = HttpProtocols.Http2`). Prometheus scrapes via HTTP/1.1.

**Solution**: Use prometheus-net's `KestrelMetricServer` to host a separate HTTP/1.1 listener on port 9091. This is independent of the main gRPC Kestrel instance.

### Implementation Approach

Add a new `MetricsService` BackgroundService in tsx-aggregator that:
1. Periodically (every 5 minutes) requests data from `IStocksDataRequestsProcessor` and `IQuoteService` using the same internal path that `StockDataSvc.GetStocksData()` uses
2. Constructs `CompanyFullDetailReport` objects and computes `OverallScore` for each
3. Updates Prometheus gauges with the results
4. Separately, also requests dashboard stats (from `StocksDataRequestsProcessor.ProcessGetDashboardStatsRequest()`) for instrument counts and timestamps

The `CompanyFullDetailReport` constructor and `OverallScore` property are in `tsx-aggregator.models`, which is already referenced by tsx-aggregator. No code needs to move between projects.

The `MetricsService` would be toggled via `HostedServicesOptions` (same pattern as existing services), adding `RunMetricsService: true` to `appsettings.json`.

---

## Q3: Can Prometheus + Grafana Surface a Ticker Symbol List?

### YES — using the info metric pattern

Prometheus metrics are numeric, but the **info metric pattern** allows exposing string metadata as labels on a gauge with value 1:

```
tsx_score13_company_info{instrument_symbol="RY.TO", company_name="Royal Bank of Canada"} 1
tsx_score13_company_info{instrument_symbol="TD.TO", company_name="Toronto-Dominion Bank"} 1
```

In prometheus-net:
```csharp
var score13Info = Metrics.CreateGauge(
    "tsx_score13_company_info",
    "Score-13 company (value is always 1, labels carry the data)",
    labelNames: new[] { "instrument_symbol", "company_name" });

// For each score-13 company:
score13Info.WithLabels(symbol, companyName).Set(1);
// Remove companies no longer score-13:
score13Info.RemoveLabelled(oldSymbol, oldCompanyName);
```

### Grafana Visualization

In Grafana, display this as a **Table panel** with an Instant query:
```promql
max(tsx_score13_company_info) by (instrument_symbol, company_name)
```

Use the **Organize fields** transformation to hide the Time and Value columns, leaving just `instrument_symbol` and `company_name`.

### Cardinality

Score-13 companies are typically a handful (0–20). This is well within safe cardinality limits. Even the full score distribution (gauges per score bracket) would be at most 14 metrics (scores 0–13).

### Conclusion: **YES**, the ticker list can be surfaced in Grafana via Prometheus info metrics without direct DB access.

---

## Q4: Email Alerts with Score-13 Diff

### Evaluation of Options

**Option A (Grafana alerting)**: **Impractical for diff**
- Grafana CAN include label values in email templates using Go templates: `{{ .Labels.instrument_symbol }}`
- Grafana alerts CAN fire when the score-13 count changes (threshold on gauge)
- However, Grafana alerts evaluate **current metric values**, not diffs. There is no built-in mechanism to compute "old list vs new list" or show added/removed tickers
- Grafana notification templates access `.Labels` and `.Annotations` of the firing alert, not historical state
- **Verdict**: Grafana can alert on "score-13 count > 0" but cannot produce the old-vs-new diff the user requires

**Option B (.NET email service)**: **Best fit for diff requirement**
- The .NET app can maintain state (previous score-13 list) and compute the diff
- Change detection is straightforward: compare sorted ticker lists, compute set difference
- Full control over email content (old list, new list, added, removed)

**Option C (hybrid)**: **Recommended**
- Numeric metrics (counts, timestamps) → Prometheus → Grafana dashboards
- Ticker list → Prometheus info metric → Grafana Table panel (for visual display)
- Email alerts with diff → .NET BackgroundService in tsx-aggregator (independent of Grafana)

### Recommendation: **Option C (hybrid)**

### Email Service Design Details

**Previous list storage**: In-memory is sufficient. On restart, the first computation establishes the baseline (no diff alert sent). Alternatively, persist to a simple JSON file or a new DB table for cross-restart continuity.

**Recipient email addresses**: Store in `appsettings.json` under a new config section (e.g., `AlertSettings.Recipients`). Simple string array — no subscription management needed.

**Email library**: **MailKit** (v4.x) — the recommended replacement for the obsolete `System.Net.Mail.SmtpClient`. MailKit supports modern TLS, OAuth2, and is actively maintained.

NuGet package: `MailKit` (add to `Directory.Packages.props`)

**SMTP options**:
- **Gmail SMTP with app password**: Free, `smtp.gmail.com:587`, TLS. User creates an app-specific password in Google account settings. Simplest for a single developer.
- **Local SMTP relay container**: e.g., `namshi/smtp` or `mailhog/mailhog` (for testing). Adds complexity.
- **Recommendation**: Gmail SMTP for production, MailHog for local testing.

**Email content format**:
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

---

## Q5: Docker Services and Integration

### Required Docker Services

| Service | Image | Port | Purpose |
|---------|-------|------|---------|
| Prometheus | `prom/prometheus:latest` | 9090 | Metrics storage & query |
| Grafana | `grafana/grafana:latest` | 3000 | Dashboard visualization |

No SMTP relay container needed if using Gmail SMTP directly from the .NET app.

### Host-to-Docker Scraping

The .NET apps run on the Windows host, not in Docker. Prometheus (in Docker) needs to scrape the metrics endpoint.

On **Windows with Docker Desktop**, `host.docker.internal` resolves to the host machine. The Prometheus `prometheus.yml` config:

```yaml
global:
  scrape_interval: 30s

scrape_configs:
  - job_name: 'tsx-aggregator'
    static_configs:
      - targets: ['host.docker.internal:9091']
```

Port 9091 is the `KestrelMetricServer` port on the tsx-aggregator.

### Grafana Provisioning

File-provisioned for version control:

**Datasource** (`docker-scripts/grafana/provisioning/datasources/prometheus.yaml`):
```yaml
apiVersion: 1
datasources:
  - name: Prometheus
    uid: tsx-prometheus-ds
    type: prometheus
    url: http://tsx-prometheus:9090
    isDefault: true
    editable: false
```

**Dashboard provider** (`docker-scripts/grafana/provisioning/dashboards/dashboards.yaml`):
```yaml
apiVersion: 1
providers:
  - name: TSX Dashboards
    type: file
    options:
      path: /etc/grafana/provisioning/dashboards/json
```

### Port Allocation

| Port | Service | Status |
|------|---------|--------|
| 3000 | Grafana | New |
| 5000/5001 | stock-market-webapi | Existing |
| 5433 | PostgreSQL (external) | Existing |
| 7001 | tsx-aggregator gRPC | Existing |
| 8081 | pgAdmin | Existing |
| 9090 | Prometheus | New |
| 9091 | tsx-aggregator metrics | New |

No port conflicts.

### Docker Compose Additions

Add to `docker-scripts/docker-compose.yml`:
```yaml
  prometheus:
    image: prom/prometheus:${PROMETHEUS_VERSION}
    container_name: tsx-prometheus
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus/prometheus.yml:/etc/prometheus/prometheus.yml
      - prometheus-data:/prometheus
    extra_hosts:
      - "host.docker.internal:host-gateway"
    restart: unless-stopped
    networks:
      - tsx-data-network

  grafana:
    image: grafana/grafana:${GRAFANA_VERSION}
    container_name: tsx-grafana
    ports:
      - "3000:3000"
    volumes:
      - grafana-data:/var/lib/grafana
      - ./grafana/provisioning:/etc/grafana/provisioning
    restart: unless-stopped
    networks:
      - tsx-data-network
```

Add `prometheus-data` and `grafana-data` volumes. Add version vars to `.env`.

---

## Q6: Metrics to Export

### From Score Computation (new, via MetricsService)

| Metric Name | Type | Labels | Source | Update Frequency |
|-------------|------|--------|--------|-----------------|
| `tsx_score13_count` | Gauge | — | Count of companies with OverallScore = 13 | Every 5 min |
| `tsx_score13_company_info` | Gauge (info) | `instrument_symbol`, `company_name` | Score-13 companies | Every 5 min |
| `tsx_score_distribution` | Gauge | `score` (0–13) | Count per score bracket | Every 5 min |
| `tsx_companies_total` | Gauge | — | Total companies with processed reports | Every 5 min |
| `tsx_companies_with_price` | Gauge | — | Companies where QuoteService has a price | Every 5 min |

### From Dashboard Stats (existing, via StocksDataRequestsProcessor)

| Metric Name | Type | Labels | Source | Update Frequency |
|-------------|------|--------|--------|-----------------|
| `tsx_active_instruments` | Gauge | — | `DashboardStatsDto.TotalActiveInstruments` | Every 5 min |
| `tsx_obsoleted_instruments` | Gauge | — | `DashboardStatsDto.TotalObsoletedInstruments` | Every 5 min |
| `tsx_instruments_with_reports` | Gauge | — | `DashboardStatsDto.InstrumentsWithProcessedReports` | Every 5 min |
| `tsx_unprocessed_events` | Gauge | — | `DashboardStatsDto.UnprocessedEventCount` | Every 5 min |
| `tsx_last_raw_ingestion_timestamp` | Gauge | — | `DashboardStatsDto.MostRecentRawIngestion` (as Unix timestamp) | Every 5 min |
| `tsx_last_aggregation_timestamp` | Gauge | — | `DashboardStatsDto.MostRecentAggregation` (as Unix timestamp) | Every 5 min |

### Data Source Notes

- **Score metrics** require calling `StocksDataRequestsProcessor` for DB data + `QuoteService` for prices, then constructing `CompanyFullDetailReport` objects. This is the same path as `StockDataSvc.GetStocksData()` followed by the `CompanyFullDetailReport` constructor. Note: `StockDataSvc.GetStocksData()` removes items that have no price from the reply (`StockDataService.cs:88`). The MetricsService should replicate this — companies without prices cannot have complete scores computed (5 of 13 checks require `PricePerShare`).
- **Dashboard stats** can be obtained by calling `StocksDataRequestsProcessor.ProcessGetDashboardStatsRequest()` — this is already an existing code path with its own SQL query (`GetDashboardStatsStmt.cs`).
- **No new database queries** are needed. All data comes from existing code paths.

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────┐
│                    tsx-aggregator (host)                 │
│                                                         │
│  ┌─────────────────┐  ┌──────────────┐                  │
│  │ RawCollector     │  │ Aggregator   │                  │
│  └────────┬────────┘  └──────┬───────┘                  │
│           │                  │                           │
│           ▼                  ▼                           │
│  ┌─────────────────────────────────────┐                │
│  │        PostgreSQL (Docker:5433)      │                │
│  └─────────────────────────────────────┘                │
│           │                                              │
│           ▼                                              │
│  ┌─────────────────┐  ┌──────────────┐                  │
│  │ StocksDataReqs   │  │ QuoteService │                  │
│  │ Processor        │  │ (prices)     │                  │
│  └────────┬────────┘  └──────┬───────┘                  │
│           │                  │                           │
│           ▼                  ▼                           │
│  ┌─────────────────────────────────────┐                │
│  │         MetricsService (NEW)         │                │
│  │  - Computes scores every 5 min       │                │
│  │  - Updates Prometheus gauges          │                │
│  │  - Detects score-13 list changes      │                │
│  │  - Sends email alerts with diff       │                │
│  └────────┬──────────────────┬──────────┘                │
│           │                  │                           │
│    ┌──────▼──────┐    ┌──────▼──────┐                    │
│    │ /metrics     │    │ Email (SMTP)│                    │
│    │ :9091        │    │ via MailKit │                    │
│    └──────┬──────┘    └──────┬──────┘                    │
│           │                  │                           │
└───────────┼──────────────────┼───────────────────────────┘
            │                  │
            ▼                  ▼
   ┌────────────────┐    ┌──────────────┐
   │ Prometheus      │    │ Gmail SMTP   │
   │ (Docker:9090)   │    │ (external)   │
   └────────┬───────┘    └──────────────┘
            │
            ▼
   ┌────────────────┐
   │ Grafana         │
   │ (Docker:3000)   │
   │  - Score-13     │
   │    count panel  │
   │  - Ticker list  │
   │    table panel  │
   │  - Instrument   │
   │    stats panels │
   └────────────────┘
```

---

## Risks and Concerns

1. **Score computation performance**: Computing scores for all ~1000 companies every 5 minutes involves: one DB query (processed reports), one QuoteService price lookup, constructing ~1000 `CompanyFullDetailReport` objects. This is lightweight — same work the web API does on each `/companies/dashboard/aggregates` request.

2. **QuoteService readiness**: The `MetricsService` must wait for `QuoteService.QuoteServiceReady` before computing scores (same pattern as `StockDataSvc.GetStocksData()`). If QuoteService is down, scores cannot be computed — metrics will be stale until it recovers.

3. **Metric cardinality**: Score-13 info metrics have very low cardinality (0–20 companies). Score distribution has 14 fixed time series (scores 0–13). No cardinality concerns.

4. **Email on restart**: If the previous score-13 list is stored in-memory only, restarting the service loses it. The first computation after restart establishes a new baseline without sending a diff alert. This is acceptable — alternatively, persist the list to a file or DB table.

5. **SMTP reliability**: Gmail SMTP has daily sending limits (500 emails/day for personal accounts). For a low-volume alert system (at most a few emails per day when scores change), this is well within limits.

6. **`host.docker.internal` on Windows**: Works on Docker Desktop for Windows. If deployed to Linux Docker, use `extra_hosts: ["host.docker.internal:host-gateway"]` (supported since Docker 20.10+).

7. **Price data timing**: QuoteService fetches prices every 2 hours from Google Sheets. If MetricsService computes scores between price updates, the scores reflect the last fetched prices. This matches the existing web API behavior and is acceptable.

---

## Summary of Recommendations

| Decision | Recommendation |
|----------|---------------|
| Metrics library | **prometheus-net** (v8.2.1) |
| Metrics host process | **tsx-aggregator** (has all data sources) |
| Metrics endpoint | **KestrelMetricServer on port 9091** (separate from gRPC) |
| Ticker list in Grafana | **YES** — info metric pattern with Table panel |
| Email alerts | **.NET-managed** in MetricsService (Option C hybrid) |
| Email library | **MailKit** |
| Previous list storage | **In-memory** (acceptable for single-process deployment) |
| SMTP provider | **Gmail SMTP with app password** |
| Docker services | **Prometheus + Grafana** (added to existing docker-compose) |

## Metadata
### Status
success
### Dependencies
- `src/tsx-aggregator/Services/StockDataService.cs` — existing gRPC data flow pattern to replicate in MetricsService
- `src/tsx-aggregator.models/AggregatorData/CompanyFullDetailReport.cs` — score computation logic
- `src/tsx-aggregator/Program.cs` — service registration and Kestrel configuration
- `src/tsx-aggregator.models/HostedServicesOptions.cs` — pattern for toggling background services
- `docker-scripts/docker-compose.yml` and `docker-scripts/.env` — Docker stack configuration
### Open Questions
- Should the previous score-13 list be persisted across restarts (DB table or file), or is in-memory sufficient?
- What email address(es) should receive alerts? (Will be configured via `appsettings.json`)
- Should the metrics update interval be configurable (default 5 min), or hardcoded?
### Assumptions
- The .NET apps continue to run on the host (not in Docker)
- Docker Desktop for Windows is used (for `host.docker.internal` resolution)
- Gmail SMTP with app password is acceptable for the user's email alerting needs
- A 5-minute metrics update interval provides sufficient freshness
- In-memory storage for previous score-13 list is acceptable (no cross-restart diff detection)
