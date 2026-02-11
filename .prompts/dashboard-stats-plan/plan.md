# Plan: Dashboard Stats Page

## Checkpoints

### Checkpoint 1: Database Layer — SQL Statement + DbmService Method

**Build:**

Create `src/dbm-persistence/Statements/GetDashboardStatsStmt.cs`:
- Extends `QueryDbStmtBase`
- SQL uses CTEs to gather all stats in a single query (see research.md section 6 for proposed SQL)
- Returns a single row with: total_active_instruments, total_obsoleted_instruments, instruments_with_processed_reports, most_recent_raw_ingestion, most_recent_aggregation, unprocessed_event_count, manual_review_count
- Cache column ordinals in static fields (follow `GetApplicationCommonStateStmt` pattern)
- `ProcessCurrentRow()` returns `false` (single-row result)
- Expose result via a public DTO property

Create `src/dbm-persistence/Statements/GetRawReportCountsByTypeStmt.cs`:
- Extends `QueryDbStmtBase`
- SQL: `SELECT report_type, COUNT(*) AS cnt FROM instrument_reports WHERE is_current = true AND ignore_report = false GROUP BY report_type`
- Returns list of (report_type, count) pairs
- Expose result via `IReadOnlyList<(int ReportType, long Count)>` property

Create DTO `src/tsx-aggregator.models/DashboardStatsDto.cs`:
- Record with fields matching the SQL output columns
- Include a `Dictionary<int, long> RawReportCountsByType` for the per-type counts

Add to `IDbmService` / `DbmService`:
- `ValueTask<(Result, DashboardStatsDto?)> GetDashboardStats(CancellationToken ct);`
- Implementation creates both statements, executes with retry, combines results into DTO

**Test:**
- Unit test for `GetDashboardStatsStmt` — verify SQL is well-formed, verify `ClearResults()` resets state
- Unit test for `GetRawReportCountsByTypeStmt` — same
- Follow existing test patterns in `src/tsx-aggregator.tests/`

**Verify:** `dotnet build dbm-persistence/dbm-persistence.csproj` and `dotnet build tsx-aggregator.models/tsx-aggregator.models.csproj` compile cleanly. `dotnet test tsx-aggregator.tests/` passes.

**Files:**
- Create: `src/dbm-persistence/Statements/GetDashboardStatsStmt.cs`
- Create: `src/dbm-persistence/Statements/GetRawReportCountsByTypeStmt.cs`
- Create: `src/tsx-aggregator.models/DashboardStatsDto.cs`
- Modify: `src/dbm-persistence/IDbmService.cs`
- Modify: `src/dbm-persistence/DbmService.cs`

---

### Checkpoint 2: gRPC Layer — Proto, Service, Processor

**Build:**

Modify `src/tsx-aggregator.shared/Protos/requests.proto`:
- Add new RPC: `rpc GetDashboardStats(GetDashboardStatsRequest) returns (GetDashboardStatsReply);`
- `GetDashboardStatsRequest` — empty message (no parameters needed)
- `GetDashboardStatsReply` — `bool success`, `string error_message`, plus all stat fields (int64 for counts, `google.protobuf.Timestamp` for dates), `repeated RawReportCount raw_report_counts`
- `RawReportCount` — `int32 report_type`, `int64 count`

Create request input class in `src/tsx-aggregator/Services/StocksDataRequestsInputs.cs`:
- `GetDashboardStatsRequestInput : StocksDataRequestsInputBase` — minimal, no extra fields

Add handler to `src/tsx-aggregator/Services/StocksDataRequestsProcessor.cs`:
- New case in the main switch for `GetDashboardStatsRequestInput`
- `ProcessGetDashboardStatsRequest()` — calls `_dbm.GetDashboardStats(ct)`, maps DTO to gRPC reply, sets TaskCompletionSource

Add gRPC method to `src/tsx-aggregator/Services/StockDataSvc.cs`:
- `GetDashboardStats()` — follow existing pattern (request ID, logging scope, post to processor, await completion)
- This endpoint does NOT need QuoteService or SearchService, so skip the `WaitAsync` for QuoteServiceReady

**Test:**
- Unit test for the processor method — mock `IDbmService.GetDashboardStats()`, verify reply is correctly constructed
- Follow existing test patterns

**Verify:** `dotnet build tsx-aggregator/tsx-aggregator.csproj` compiles cleanly. `dotnet test tsx-aggregator.tests/` passes.

**Files:**
- Modify: `src/tsx-aggregator.shared/Protos/requests.proto`
- Modify: `src/tsx-aggregator/Services/StocksDataRequestsInputs.cs`
- Modify: `src/tsx-aggregator/Services/StocksDataRequestsProcessor.cs`
- Modify: `src/tsx-aggregator/Services/StockDataSvc.cs`

---

### Checkpoint 3: REST API Layer — Endpoint + DTO

**Build:**

Add endpoint to `src/stock-market-webapi/Controllers/CompaniesController.cs`:
- `[HttpGet("dashboard")]` returning `ActionResult<DashboardStatsResponse>`
- Calls `_client.GetDashboardStatsAsync(new GetDashboardStatsRequest())`
- Maps gRPC reply fields to response DTO
- Computes `InstrumentsWithoutProcessedReports = TotalActiveInstruments - InstrumentsWithProcessedReports`

Create response DTO (record in `src/tsx-aggregator.models/` or inline in controller file, depending on complexity):
- `DashboardStatsResponse` record with all stats as properties
- Includes human-readable report type names (map int enum to string: 1→"Cash Flow", 2→"Income Statement", 3→"Balance Sheet")

**Test:**
- Unit test for the controller endpoint — mock gRPC client, verify correct mapping and HTTP response

**Verify:** `dotnet build stock-market.webapi/stock-market.webapi.csproj` compiles cleanly. `dotnet test tsx-aggregator.tests/` passes.

**Files:**
- Modify: `src/stock-market-webapi/Controllers/CompaniesController.cs`
- Create or Modify: `src/tsx-aggregator.models/DashboardStatsResponse.cs` (if separate file)

---

### Checkpoint 4: Angular Frontend — Dashboard Component

**Build:**

Create `deep-value/src/app/dashboard/dashboard.component.ts`:
- Properties: `loading: boolean`, `errorMsg: string`, `stats: DashboardStats | null`
- `ngOnInit()` calls `loadStats()`
- `loadStats()` calls `companyService.getDashboardStats().subscribe({...})`
- `refreshData()` method for refresh button

Create `deep-value/src/app/models/dashboard-stats.ts`:
- `DashboardStats` class with all stat fields
- Include a helper to map report type int to display name

Create `deep-value/src/app/dashboard/dashboard.component.html`:
- Page header with title "Dashboard" and refresh button (same pattern as company list pages)
- Card-based layout using `mat-card` for stat groups:
  - **Instruments** card: active count, obsoleted count, with processed, without processed
  - **Reports** card: raw report counts by type, manual review count
  - **Processing** card: most recent raw ingestion, most recent aggregation, unprocessed events
  - **Schedule** card: next directory fetch, next quote fetch (from FSM state — note: this uses the existing `GetApplicationCommonState` endpoint, or we include it in our new endpoint)
- Loading spinner/text and error message display

Create `deep-value/src/app/dashboard/dashboard.component.scss`:
- Card grid layout (CSS Grid or Flexbox)
- Stat value styling (large number, small label)

Add service method to `deep-value/src/app/services/company.service.ts`:
- `getDashboardStats(): Observable<DashboardStats>`
- Calls `GET /companies/dashboard`

Modify `deep-value/src/app/app.module.ts`:
- Import `MatCardModule` from `@angular/material/card`
- Add `DashboardComponent` to declarations

Modify `deep-value/src/app/app-routing.module.ts`:
- Add route: `{ path: 'dashboard', component: DashboardComponent }`

Modify `deep-value/src/app/app.component.html`:
- Add nav link: `<a mat-list-item routerLink="/dashboard" routerLinkActive="active">Dashboard</a>`
- Place it near the top of the nav list (after Home)

**Test:**
- Angular component test (`dashboard.component.spec.ts`) — verify component creates, verify loading state, verify data binding with mock service

**Verify:** `cd deep-value && npm run build` compiles cleanly. `npm test` passes (if test runner is configured).

**Files:**
- Create: `deep-value/src/app/dashboard/dashboard.component.ts`
- Create: `deep-value/src/app/dashboard/dashboard.component.html`
- Create: `deep-value/src/app/dashboard/dashboard.component.scss`
- Create: `deep-value/src/app/models/dashboard-stats.ts`
- Modify: `deep-value/src/app/services/company.service.ts`
- Modify: `deep-value/src/app/app.module.ts`
- Modify: `deep-value/src/app/app-routing.module.ts`
- Modify: `deep-value/src/app/app.component.html`

---

## Metadata
### Status
success
### Dependencies
- Database schema (R__001__CreateTables.sql) must not change
- Existing gRPC/REST endpoint patterns
- Angular Material available modules
### Open Questions
- FSM schedule info (next fetch times): include in the dashboard SQL query, or reuse existing `GetApplicationCommonState`? Recommendation: include in dashboard query to avoid a second round-trip.
- Should dashboard replace the Home page or be a separate nav item? Recommendation: separate nav item for v1.
### Assumptions
- On-demand queries are fast enough (no caching needed for ~1500-2000 instruments)
- MatCardModule will be added for card-based layout
- No i18n needed for v1 dashboard (hardcoded English labels); can be added later
