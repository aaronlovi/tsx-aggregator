# Research: Dashboard Stats Page

## 1. Database Schema

**Core tables and relevant columns:**

| Table | Key Columns | Notes |
|-------|-------------|-------|
| `instruments` | instrument_id (PK), exchange, company_symbol, instrument_symbol, company_name, created_date, obsoleted_date | Active instruments have `obsoleted_date IS NULL` |
| `instrument_reports` | instrument_report_id (PK), instrument_id, report_type (int), report_period_type (int), report_json, report_date, created_date, obsoleted_date, is_current, check_manually, ignore_report | Raw scraped financial reports |
| `processed_instrument_reports` | instrument_id, report_json, created_date, obsoleted_date | Aggregated reports; active have `obsoleted_date IS NULL` |
| `instrument_prices` | instrument_id, price_per_share, num_shares, created_date, obsoleted_date | Stock prices from Google Sheets |
| `instrument_events` | instrument_id, event_date, event_type (int), is_processed | FSM event log |
| `raw_instrument_processing_state` | instrument_id, start_date, is_complete | Per-instrument raw collector state |
| `state_fsm_state` | next_fetch_directory_time, next_fetch_instrument_data_time, prev_company_symbol, prev_instrument_symbol, next_fetch_stock_quote_time | Singleton FSM state |

**Enums:** report_type: 1=CashFlow, 2=IncomeStatement, 3=BalanceSheet. report_period_type: 1=Annual, 2=Quarterly, 3=SemiAnnual. event_type: 1=NewListedCompany, 2=UpdatedListedCompany, 3=ObsoletedListedCompany, 4=RawDataChanged.

**Indexes:** `idx_instrument_report(instrument_id, report_type, report_period_type, report_date)`, `idx_processed_report(instrument_id, obsoleted_date)`, `idx_instrument_prices(instrument_id, created_date)`, `idx_instrument_events(instrument_id)`.

## 2. Proposed Dashboard Stats

### Fast queries (indexed, small result sets)
1. **Total active instruments** — `SELECT COUNT(*) FROM instruments WHERE obsoleted_date IS NULL`
2. **Instruments by exchange** — `SELECT exchange, COUNT(*) FROM instruments WHERE obsoleted_date IS NULL GROUP BY exchange`
3. **Total obsoleted instruments** — `SELECT COUNT(*) FROM instruments WHERE obsoleted_date IS NOT NULL`
4. **Instruments with processed reports** — `SELECT COUNT(DISTINCT pir.instrument_id) FROM processed_instrument_reports pir WHERE pir.obsoleted_date IS NULL`
5. **Instruments without processed reports** — Difference of active instruments minus those with processed reports
6. **Most recent raw report ingestion** — `SELECT MAX(created_date) FROM instrument_reports`
7. **Most recent aggregation run** — `SELECT MAX(created_date) FROM processed_instrument_reports`
8. **Unprocessed events** — `SELECT COUNT(*) FROM instrument_events WHERE is_processed = false`
9. **Reports flagged for manual review** — `SELECT COUNT(*) FROM instrument_reports WHERE check_manually = true AND is_current = true`
10. **FSM state** — `SELECT * FROM state_fsm_state` (singleton, tells when next scrape/quote fetch is)

### Moderate queries (aggregation over larger sets, but still reasonable)
11. **Raw reports by type** — `SELECT report_type, COUNT(*) FROM instrument_reports WHERE is_current = true AND ignore_report = false GROUP BY report_type`
12. **Total raw reports vs processed reports** — Simple counts on each table

### Potentially slow queries (require parsing JSON or full table scans)
13. **Score distribution** — Requires parsing `report_json` from `processed_instrument_reports`. The overall score is computed in application code (C# `CompanyFullDetailReport`), not stored as a column. This would need the full GetProcessedStockDataByExchange flow.
14. **Average/median market cap** — Also requires joining price data and computing in application code.

### Recommendation
Stats 1-12 can be served as **on-demand SQL queries** (fast, indexed). Stats 13-14 (score distribution, market cap stats) should either:
- (a) Reuse the existing `GetProcessedStockDataByExchange` gRPC call and compute in the REST controller/frontend, or
- (b) Be omitted from v1 and added later with a materialized view or cached computation.

**Recommended approach: on-demand for v1.** The dataset is small enough (TSX has ~1500-2000 instruments) that COUNT/GROUP BY queries will be fast. Score distribution can piggyback on the existing companies endpoint. No scheduled background job needed for v1.

## 3. Backend Patterns

### SQL Statement Classes (`src/dbm-persistence/Statements/`)
- **Naming:** `{Action}{Entity}Stmt.cs` (e.g., `GetInstrumentListStmt.cs`)
- **Base classes:** `QueryDbStmtBase` for SELECT, `NonQueryDbStmtBase` for single INSERT/UPDATE, `NonQueryBatchedDbStmtBase` for batched writes
- **Pattern:** Constructor takes parameters → `GetBoundParameters()` returns NpgsqlParameters → `ProcessCurrentRow(reader)` builds results → results stored as instance properties
- **Column ordinals** cached in static fields for performance
- **Execution:** via `PostgresExecutor.ExecuteWithRetry(stmt, ct)` which handles connection pooling (semaphore-limited) and retry on connection errors

### DbmService (`src/dbm-persistence/DbmService.cs`)
- Implements `IDbmService` interface
- Methods return `ValueTask<(Result, T)>` or `ValueTask<Result<T>>`
- Creates statement instance, calls `_exec.ExecuteWithRetry()`, returns result + statement properties

### gRPC Endpoint Pattern
1. Define RPC + messages in `src/tsx-aggregator.shared/Protos/requests.proto`
2. Implement in `src/tsx-aggregator/Services/StockDataSvc.cs` — creates request object, posts to processor channel, awaits TaskCompletionSource
3. Processor handles in `src/tsx-aggregator/Services/StocksDataRequestsProcessor.cs` — reads from channel, calls DbmService, builds reply, sets TaskCompletionSource
4. Request input classes in `src/tsx-aggregator/Services/StocksDataRequestsInputs.cs`

### REST Endpoint Pattern
- `src/stock-market-webapi/Controllers/CompaniesController.cs`
- Calls gRPC client (`_client.GetXxxAsync()`), checks `reply.Success`, transforms to DTO, returns `Ok(dto)` or `BadRequest()`
- DTOs in `src/stock-market-webapi/Models/` (e.g., `CompanyFullDetailReport`, `CompanySummaryReport`)

## 4. Frontend Patterns

### Adding a New Page
1. Create component in `deep-value/src/app/{name}/` (`.ts`, `.html`, `.scss`)
2. Add to `declarations` in `deep-value/src/app/app.module.ts`
3. Add route in `deep-value/src/app/app-routing.module.ts`
4. Add nav link in `deep-value/src/app/app.component.html` (`<a mat-list-item routerLink="/path">`)

### Available Material Modules (already imported)
MatButton, MatIcon, MatTable, MatSidenav, MatToolbar, MatList, MatExpansion, MatFormField, MatInput, MatAutocomplete, MatTooltip

**Not imported but potentially useful for dashboard:** MatCard (`MatCardModule`), MatProgressBar, MatProgressSpinner, MatChips, MatGridList. Would need to add imports to app.module.ts.

### Data Loading Pattern
- Component has `loading: boolean`, `errorMsg: string`, data properties
- `ngOnInit()` calls load method
- Load method: set loading=true, call `companyService.getXxx().subscribe({next: ..., error: ...})`
- CompanyService methods call `this.http.get<T>(url).pipe(map(...))`

### i18n
- Add keys to `TextService` (`deep-value/src/app/services/text.service.ts`)
- Add translations to `en-US.json` and `zh-CN.json`
- Use in templates: `{{ textService.key_name | translate }}`

## 5. Risks and Concerns

1. **Score distribution** requires the full companies fetch + application-level computation. For v1, consider computing this on the frontend from the existing top/bottom/all companies data, or adding a dedicated endpoint.
2. **No MatCard imported** — need to add `MatCardModule` to app.module.ts for a card-based dashboard layout.
3. **Data staleness** — dashboard stats reflect DB state at query time. For a production system, caching with TTL would reduce DB load, but not needed for v1 given small data volume.

## 6. Recommended Approach

### Architecture: Direct on-demand queries (no caching/scheduling for v1)

**New files to create:**
- `src/dbm-persistence/Statements/GetDashboardStatsStmt.cs` — single SQL query returning counts
- `src/tsx-aggregator/Services/` — new request input type for dashboard stats
- `src/tsx-aggregator.shared/Protos/requests.proto` — new RPC + messages
- `src/stock-market-webapi/Controllers/CompaniesController.cs` — new endpoint (or separate DashboardController)
- `deep-value/src/app/dashboard/` — new Angular component
- `deep-value/src/app/models/dashboard-stats.ts` — TypeScript model

**Proposed SQL (single query, multiple CTEs):**
```sql
WITH active_instruments AS (
    SELECT COUNT(*) AS cnt FROM instruments WHERE obsoleted_date IS NULL
),
obsoleted_instruments AS (
    SELECT COUNT(*) AS cnt FROM instruments WHERE obsoleted_date IS NOT NULL
),
instruments_with_processed AS (
    SELECT COUNT(DISTINCT instrument_id) AS cnt FROM processed_instrument_reports WHERE obsoleted_date IS NULL
),
raw_report_counts AS (
    SELECT report_type, COUNT(*) AS cnt FROM instrument_reports WHERE is_current = true AND ignore_report = false GROUP BY report_type
),
latest_raw AS (
    SELECT MAX(created_date) AS dt FROM instrument_reports
),
latest_processed AS (
    SELECT MAX(created_date) AS dt FROM processed_instrument_reports
),
unprocessed_events AS (
    SELECT COUNT(*) AS cnt FROM instrument_events WHERE is_processed = false
),
manual_review AS (
    SELECT COUNT(*) AS cnt FROM instrument_reports WHERE check_manually = true AND is_current = true
)
SELECT
    (SELECT cnt FROM active_instruments) AS total_active_instruments,
    (SELECT cnt FROM obsoleted_instruments) AS total_obsoleted_instruments,
    (SELECT cnt FROM instruments_with_processed) AS instruments_with_processed_reports,
    (SELECT dt FROM latest_raw) AS most_recent_raw_ingestion,
    (SELECT dt FROM latest_processed) AS most_recent_aggregation,
    (SELECT cnt FROM unprocessed_events) AS unprocessed_event_count,
    (SELECT cnt FROM manual_review) AS manual_review_count
```

Plus a secondary query for raw report counts by type (returns multiple rows).

**Proposed dashboard stats for v1:**
1. Total active instruments
2. Total obsoleted instruments
3. Instruments with processed reports
4. Instruments without processed reports (computed: active - with_processed)
5. Most recent raw report ingestion date
6. Most recent aggregation date
7. Unprocessed events count
8. Reports flagged for manual review
9. Raw reports by type (CashFlow, BalanceSheet, IncomeStatement)
10. Next scheduled directory fetch / quote fetch (from FSM state)

## Metadata
### Status
success
### Dependencies
- Database schema (R__001__CreateTables.sql)
- Existing gRPC/REST endpoint patterns
- Angular Material module imports
### Open Questions
- Should score distribution be included in v1? (requires full companies fetch or new DB column)
- Should the dashboard be the landing page or a separate nav item?
### Assumptions
- Dataset is small enough (~1500-2000 instruments) that on-demand queries are fast
- No caching or scheduled computation needed for v1
- MatCardModule will be added for card-based layout
