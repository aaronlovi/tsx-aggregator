# Research Findings: Simplify Report Versioning to Merge-Override Model

## Current Version Lifecycle

```
1. RawCollector scrapes TMX Money for financial reports (TsxCompanyProcessor)
2. RawFinancialDeltaTool.TakeDelta() compares new reports vs existing (by report_type + period + date)
3. If identical → skip
4. If different AND FeatureFlag CheckExistingRawReportUpdates = true:
     → INSERT new row with check_manually=true, keep old row as-is
     → Old row: is_current=true, check_manually=false
     → New row: is_current=true, check_manually=true
     → Both rows now share the same (instrument_id, report_type, report_period_type, report_date)
     → An instrument_event (RawDataChanged) is NOT inserted (only check_manually reports, no actual inserts)
5. If different AND FeatureFlag = false:
     → Old row: is_current=false, obsoleted_date=now (via ObsoleteSql)
     → New row: is_current=true, check_manually=false
     → instrument_event (RawDataChanged, is_processed=false) is inserted
6. User reviews via "Updated Raw Data Reports" UI:
     → UI shows instruments with DUPLICATE rows (same type/period/date, not ignored)
     → User picks which report to "keep" and which to "ignore"
     → POST /companies/ignore_raw_report/{instrumentId}/{keepReportId} with body [ignoreIds]
     → IgnoreRawDataReportStmt: ignored rows → ignore_report=true, is_current=false, check_manually=false
                                 kept row → ignore_report=false, is_current=true, check_manually=false
     → Also inserts instrument_event (RawDataChanged) to trigger re-aggregation
7. Aggregator picks up unprocessed instrument_events:
     → GetCurrentInstrumentReports: WHERE is_current=true AND check_manually=false AND ignore_report=false
     → Builds CompanyReport, serializes to JSON, writes to processed_instrument_reports
     → Marks event as processed
```

**Current production config**: `CheckExistingRawReportUpdates: false` — so the manual review path is **disabled**. Changed reports just get replaced automatically (old obsoleted, new inserted).

## Answers by Section

### Database Schema & Migrations

**Q1: R__003 and R__005 migrations**

- `R__003__AddCheckManuallyColumn.sql`: Adds `check_manually BOOLEAN NOT NULL DEFAULT false` to `instrument_reports`
- `R__005__AddIgnoreDataReportColumn.sql`: Adds `ignore_report BOOLEAN NOT NULL DEFAULT false` to `instrument_reports`

**Q2: Full instrument_reports schema**

```sql
instrument_report_id BIGINT NOT NULL PRIMARY KEY
instrument_id BIGINT NOT NULL
report_type INT NOT NULL
report_period_type INT NOT NULL
report_json TEXT NOT NULL
report_date TIMESTAMPTZ NOT NULL
created_date TIMESTAMPTZ NOT NULL
obsoleted_date TIMESTAMPTZ DEFAULT NULL
is_current BOOLEAN NOT NULL
check_manually BOOLEAN NOT NULL DEFAULT false
ignore_report BOOLEAN NOT NULL DEFAULT false
```

Index: `(instrument_id, report_type, report_period_type, report_date)`

Versioning columns: `is_current`, `check_manually`, `ignore_report`, `obsoleted_date`

**Q3: How "original" vs "updated" rows are distinguished**
They are **separate rows** in the same table, sharing the same `(instrument_id, report_type, report_period_type, report_date)` composite key. Distinguished by:

- Original: `is_current=true, check_manually=false`
- Updated (pending review): `is_current=true, check_manually=true`
- Ignored: `is_current=false, ignore_report=true, check_manually=false`
- Obsoleted: `is_current=false, obsoleted_date IS NOT NULL`

### Database Access Layer

**Q4: GetRawInstrumentsWithUpdatedDataReportsStmt**
Complex query that finds instruments with **duplicate** active report rows. The inner CTE `duplicate_reports` finds groups of `(instrument_id, report_type, report_period_type, report_date)` that have `COUNT(*) > 1` where `obsoleted_date IS NULL AND ignore_report = FALSE`. These are the "conflicting" reports shown in the UI. Results are paged via `DENSE_RANK()` and `@rank_min`/`@rank_max`.

**Q5: IgnoreRawDataReportStmt**
A batched statement with 3 SQL commands:

1. `UPDATE instrument_reports SET ignore_report=true, is_current=false, check_manually=false WHERE instrument_report_id = ANY(@ids)` — marks the "ignore" reports
2. `UPDATE instrument_reports SET ignore_report=false, is_current=true, check_manually=false WHERE instrument_report_id = @keep_id` — marks the "keep" report
3. `INSERT INTO instrument_events (RawDataChanged, is_processed=false)` — triggers re-aggregation

**Q6: UpdateInstrumentReportsStmt**
A batched statement called by RawCollector after `TakeDelta`. It:

1. Obsoletes old reports (sets `is_current=false, obsoleted_date=now`)
2. Inserts new report rows (with `is_current=true, check_manually=<from delta>`)
3. Inserts an `instrument_event` (RawDataChanged) **only if** there are non-check-manually reports to insert or reports to obsolete
4. Obsoletes old prices, inserts new prices

**Q7: GetCurrentInstrumentReportsStmt vs GetInstrumentReportsStmt**

- `GetCurrentInstrumentReportsStmt`: `WHERE is_current=true AND instrument_id=@id AND check_manually=false AND ignore_report=false` — returns only the "clean" reports, used by Aggregator
- `GetInstrumentReportsStmt`: `WHERE instrument_id=@id AND is_current=true` — returns ALL current reports including `check_manually=true` and `ignore_report=true`, used by RawCollector for consistency checks

**Q8: IDbmService versioning-related methods**

- `GetRawFinancialsByInstrumentId(long instrumentId)` → returns `CurrentInstrumentRawDataReportDto` list (same filter as GetCurrentInstrumentReportsStmt)
- `UpdateRawInstrumentReports(RawFinancialsDelta)` → executes UpdateInstrumentReportsStmt
- `GetRawInstrumentsWithUpdatedDataReports(exchange, page, size)` → lists instruments with conflicting reports
- `IgnoreRawUpdatedDataReport(RawInstrumentReportsToKeepAndIgnoreDto)` → executes IgnoreRawDataReportStmt
- `UpsertRawCurrentInstrumentReport(CurrentInstrumentRawDataReportDto)` → upserts a single report
- `ExistsMatchingRawReport(CurrentInstrumentRawDataReportDto)` → checks if an identical check_manually report already exists (dedup)

### Backend - Report Comparison & Aggregation

**Q9: RawFinancialDeltaTool**

- Input: existing `CurrentInstrumentRawDataReportDto` list + newly scraped `TsxCompanyData`
- Groups reports by (report_type, period, date), matches by year (annual) or quarter (quarterly)
- Compares field-by-field via `RawReportDataMap.IsEqual(JsonDocument)` on the report JSON
- Output: `RawFinancialsDelta` containing lists of reports to insert and reports to obsolete
- Branching based on `CheckExistingRawReportUpdates` feature flag:
  - `true`: Different report → insert with `check_manually=true`, do NOT obsolete old
  - `false`: Different report → insert normally, obsolete old

**Q10: RawFinancialDeltaToolTests**
3 test scenarios:

1. No reports → empty delta
2. Same data → no delta (no inserts, no obsoletes)
3. Changed property with `[InlineData(true, 1, 0)]` and `[InlineData(false, 1, 1)]`:
   - Flag on: 1 insert (check_manually=true), 0 obsoletes
   - Flag off: 1 insert (check_manually=false), 1 obsolete

**Q11: Models**

- `InstrumentRawReportData`: record with `InstrumentReportId, ReportCreatedDate, IsCurrent, CheckManually, IgnoreReport, ReportJson`
- `RawReportConsistencyMap`: Dictionary mapping `(ReportType, ReportPeriodType, ReportDate)` → list of `(InstrumentReportId, IsCurrent, CheckManually)`. Used to validate that an ignore request is consistent (the report to keep and all reports to ignore are current, same type/period/date, no orphaned current reports).

**Q12: Aggregator handling of versioning flags**
`ProcessCompanyDataChangedEvent` calls `GetCurrentInstrumentReports` which filters to `is_current=true AND check_manually=false AND ignore_report=false`. So the Aggregator **silently ignores** any reports pending manual review or ignored. It only processes "clean" reports.

### Re-aggregation Trigger Mechanism

**Q13: instrument_events table**

```sql
instrument_id BIGINT NOT NULL
event_date TIMESTAMPTZ NOT NULL
event_type INT NOT NULL  -- CompanyEventTypes enum: RawDataChanged, NewListedCompany, etc.
is_processed BOOLEAN NOT NULL
```

- `InsertInstrumentEventStmt`: Simple INSERT
- `GetNextInstrumentEventStmt`: SELECT oldest unprocessed event (is_processed=FALSE), JOINing instruments and prices, LIMIT 1
- `UpdateInstrumentEventStmt`: UPDATE is_processed WHERE instrument_id AND event_type

**Q14: InsertProcessedCompanyReportStmt**
A batched statement:

1. Obsolete existing processed report: `UPDATE processed_instrument_reports SET obsoleted_date=now WHERE instrument_id=@id AND obsoleted_date IS NULL`
2. Insert new processed report: `INSERT INTO processed_instrument_reports (instrument_id, report_json, created_date)`
3. Mark event as processed: `UPDATE instrument_events SET is_processed=true`

So it's an **obsolete-then-insert** pattern (not upsert), keeping history.

**Q15: Full re-aggregation flow**

1. **Trigger**: `UpdateInstrumentReportsStmt` (called by RawCollector after scraping) inserts `instrument_event(RawDataChanged, is_processed=false)` — but **only if** there are non-check-manually inserts or obsoletes. Also `IgnoreRawDataReportStmt` (called when user resolves a conflict) inserts the same event.
2. **Poll**: `Aggregator.ProcessCheckForInstrumentEvent()` calls `GetNextInstrumentEvent()` every 5s-2min
3. **Process**: `ProcessCompanyDataChangedEvent()` fetches current clean reports → builds CompanyReport → `InsertProcessedCompanyReport()` obsoletes old processed report, inserts new one, marks event processed.

**Q16: Under merge-override model**
If raw data is merged in-place (UPDATE existing row rather than INSERT new + keep old), and the RawCollector already fires `instrument_event(RawDataChanged)` when data changes, then **yes, the existing event mechanism will automatically trigger re-aggregation**. The key is ensuring `UpdateInstrumentReportsStmt` (or its replacement) still inserts the event when data changes.

### Backend - RawCollector (Scraping & Storage)

**Q13a: Trace what happens when a newly scraped report differs from the existing one**
In `RawCollector.FetchInstrumentData.cs`:

1. `ProcessFetchInstrumentData()` fetches existing reports via `_dbm.GetRawFinancialsByInstrumentId()`
2. Scrapes new data via `GetRawFinancials()` → `TsxCompanyProcessor.Create()` → `companyProcessor.GetRawFinancials()`
3. Calls `RawFinancialDeltaTool.TakeDelta()` to compare existing vs new → produces `RawFinancialsDelta`
4. Calls `DropDuplicateCheckManuallyReports(delta)` — iterates `delta.InstrumentReportsToInsert` in reverse, for any report where `CheckManually=true`, checks if an identical report already exists in DB via `_dbm.ExistsMatchingRawReport()`. If exists, removes it from the insert list (prevents duplicate pending-review rows on repeated scrapes).
5. Calls `_dbm.UpdateRawInstrumentReports(delta)` → executes `UpdateInstrumentReportsStmt` (obsolete old + insert new + fire event)

**Q13b: FSM state related to pending manual review**
No. `RawCollector.cs` FSM states do not include any "pending manual review" state. The manual review concept exists only at the data level (`check_manually` column) and in the UI, not in the FSM.

### Backend - gRPC & REST Layer

**Q17: Proto definitions**

- `rpc GetStocksWithUpdatedRawDataReports` → paginated list of instruments with conflicting reports
- `rpc IgnoreRawDataReport` → takes `instrument_id`, `instrument_report_id_to_keep`, `instrument_report_ids_to_ignore`
- `message InstrumentWithUpdatedRawDataItem`: includes `is_current`, `check_manually`, `ignore_report`, `report_json`
- Dashboard: `manual_review_count` field in `GetDashboardStatsReply`

**Q18: StocksDataRequestsProcessor**
Does NOT handle updated-reports or ignore requests. Those are routed through `StockDataSvc` (class in `StockDataService.cs`) which delegates directly to `RawCollector`.

**Q19: REST endpoints**

- `GET /companies/updated_raw_data_reports?exchange=&pageNumber=&pageSize=` → `GetStocksWithUpdatedRawDataReports` gRPC → `RawCollector`
- `POST /companies/ignore_raw_report/{instrumentId}/{instrumentReportIdToKeep}` with body `[ignoreIds]` → `IgnoreRawDataReport` gRPC → `RawCollector`

### Frontend

**Q20: UpdatedRawDataReportsComponent**

- Displays a paginated table of instruments with "conflicting" raw reports (multiple rows for same type/period/date)
- For each instrument, shows all conflicting report versions side-by-side with field-by-field comparison
- Highlights cells where values differ across versions
- User selects "keep" or "ignore" for each report via radio buttons
- "Ignore Reports" button per instrument, and "Ignore All Reports" button for batch processing
- Calls `companyService.getUpdatedRawDataReports()` and `companyService.ignoreRawDataReports()`

**Q21: CompanyService methods**

- `getUpdatedRawDataReports(exchange, pageNumber, pageSize)` → `GET /companies/updated_raw_data_reports`
- `ignoreRawDataReports(instrumentId, keepId, ignoreIds[])` → `POST /companies/ignore_raw_report/{id}/{keepId}`

**Q22: Frontend model**
`InstrumentRawReportData`: `instrumentReportId, reportCreatedDate, isCurrent, checkManually, ignoreReport, reportJson`
Also: `InstrumentWithConflictingRawData`, `InstrumentsWithConflictingRawData`

**Q23: Other frontend components**

- `app-routing.module.ts`: route for the updated-raw-data-reports component
- `app.component.html`: nav link to the updated-raw-data-reports page
- Dashboard shows `manualReviewCount` stat

## Files That Would Need to Change

### Must Change

| File | Reason |
|------|--------|
| `src/tsx-aggregator/Aggregated/RawFinancialDeltaTool.cs` | Core change: remove `_checkForReportChanges` branch, always merge new data into existing |
| `src/tsx-aggregator.tests/RawFinancialDeltaToolTests.cs` | Update tests for new merge behavior |
| `src/tsx-aggregator.models/FeatureFlagsOptions.cs` | Remove `CheckExistingRawReportUpdates` flag |
| `src/tsx-aggregator/appsettings.json` | Remove `CheckExistingRawReportUpdates` |
| `src/tsx-aggregator/appsettings.Development.json` | Remove `CheckExistingRawReportUpdates` |
| `src/tsx-aggregator.tests/FeatureFlagsOptionsTests.cs` | Update tests after removing `CheckExistingRawReportUpdates` |
| `src/dbm-persistence/Statements/UpdateInstrumentReportsStmt.cs` | Change to UPDATE report_json in-place instead of obsolete+insert pattern for changed reports |

### Can Remove or Simplify

| File | Reason |
|------|--------|
| `src/dbm-persistence/Statements/GetRawInstrumentsWithUpdatedDataReportsStmt.cs` | No more conflicting reports to list |
| `src/dbm-persistence/Statements/IgnoreRawDataReportStmt.cs` | No more ignore workflow |
| `src/tsx-aggregator.models/RawReportConsistencyMap.cs` | Consistency checking for ignore is no longer needed |
| `src/dbm-persistence/Migrations/R__003__AddCheckManuallyColumn.sql` | Column no longer needed (new migration to drop) |
| `src/dbm-persistence/Migrations/R__005__AddIgnoreDataReportColumn.sql` | Column no longer needed (new migration to drop) |
| `deep-value/src/app/updated-raw-data-reports/` (component + template + styles) | Entire UI page can be removed |
| `deep-value/src/app/models/instrument_raw_report_data.ts` | No longer needed |
| `deep-value/src/app/models/instrument_with_conflicting_raw_data.ts` | No longer needed |
| `deep-value/src/app/models/instruments_with_conflicting_raw_data.ts` | No longer needed |
| `deep-value/src/app/models/instrument_report_key.ts` | No longer needed |
| `deep-value/src/app/services/company.service.ts` | Remove `getUpdatedRawDataReports()` and `ignoreRawDataReports()` |
| `deep-value/src/app/app-routing.module.ts` | Remove route |
| `deep-value/src/app/app.component.html` | Remove nav link |
| `src/tsx-aggregator.shared/Protos/requests.proto` | Remove `GetStocksWithUpdatedRawDataReports`, `IgnoreRawDataReport`, and associated messages |
| `src/tsx-aggregator/Services/StockDataService.cs` | Remove `GetStocksWithUpdatedRawDataReports()` and `IgnoreRawDataReport()` overrides |
| `src/stock-market-webapi/Controllers/CompaniesController.cs` | Remove `GetUpdatedRawDataReports()` and `IgnoreRawReport()` endpoints |
| `src/dbm-persistence/IDbmService.cs` | Remove `GetRawInstrumentsWithUpdatedDataReports`, `IgnoreRawUpdatedDataReport`, `ExistsMatchingRawReport` |
| `src/dbm-persistence/DbmService.cs` | Same removals |
| `src/tsx-aggregator/Raw/RawCollector.FetchInstrumentData.cs` | Remove `DropDuplicateCheckManuallyReports()` method and its call |

### Need New Migration

| File | Reason |
|------|--------|
| New: `src/dbm-persistence/Migrations/R__006__RemoveCheckManuallyAndIgnoreReport.sql` | Drop `check_manually` and `ignore_report` columns, clean up any existing check_manually/ignore rows |

### Dashboard Impact

| File | Reason |
|------|--------|
| `src/dbm-persistence/Statements/GetDashboardStatsStmt.cs` | Remove `manual_review_count` query |
| `src/tsx-aggregator.models/DashboardStatsDto.cs` | Remove `ManualReviewCount` |
| `src/tsx-aggregator.models/DashboardStatsResponse.cs` | Remove `ManualReviewCount` |
| `src/tsx-aggregator.shared/Protos/requests.proto` | Remove `manual_review_count` from `GetDashboardStatsReply` (same file as above, different change) |
| `deep-value/src/app/dashboard/` | Remove display of manual review count |
| `src/tsx-aggregator.tests/DashboardStatsDtoTests.cs` | Update tests after removing `ManualReviewCount` |
| `src/tsx-aggregator.tests/DashboardStatsResponseTests.cs` | Update tests after removing `ManualReviewCount` |

## Risks & Concerns

1. **Data loss if scrape returns fewer fields**: If a new scrape returns fewer fields (e.g., a field was temporarily unavailable on the website), a naive merge would lose data. Recommendation: only **add/update** fields, never remove existing fields from `report_json` when merging.

2. **Existing check_manually rows in the database**: There may be existing rows with `check_manually=true` that have never been resolved. A migration should clean these up — either merge them into the originals or drop them.

3. **Historical data**: The current system keeps obsoleted rows (for audit trail). The merge-override model loses this history. **Decision: no audit trail needed** — just add/update datapoints silently.

4. **Re-aggregation trigger**: The current flow inserts `instrument_event(RawDataChanged)` only when non-check-manually reports are inserted/obsoleted. If we change to in-place UPDATE, we must still insert this event for any actual data change.

5. **DropDuplicateCheckManuallyReports** in `RawCollector.FetchInstrumentData.cs`: This dedup logic can be removed along with the check_manually concept.

6. **Feature flag removal**: The `CheckExistingRawReportUpdates` flag is `[Required]` in `FeatureFlagsOptions`. Removing it requires updating `appsettings.json` and tests (`FeatureFlagsOptionsTests.cs`).

## Recommended Approach

1. **Change RawFinancialDeltaTool**: When a matching existing report has different data:
   - Build a merged JSON: start with the existing report's fields, overlay all fields from the new report (add new fields, update changed values, preserve fields missing from the new scrape)
   - Instead of inserting a new row and obsoleting the old one, UPDATE the existing row's `report_json` and `created_date` in-place
   - Still insert `instrument_event(RawDataChanged)` to trigger re-aggregation

2. **Simplify UpdateInstrumentReportsStmt**: Add an UPDATE-in-place path for changed reports (update `report_json` on existing `instrument_report_id`) alongside the existing INSERT path for new reports (where no matching row existed).

3. **Remove the entire manual review subsystem**: Delete all the "check_manually", "ignore_report" infrastructure across all layers (DB columns, statements, gRPC RPCs, REST endpoints, frontend component).

4. **Remove the feature flag**: `CheckExistingRawReportUpdates` is no longer meaningful.

5. **Migration**: New migration to drop `check_manually` and `ignore_report` columns. First, clean up any existing check_manually rows (merge their data into originals or delete them).

6. **Dashboard**: Remove `manualReviewCount` from dashboard stats since it's no longer relevant.

## Metadata

### Status

success

### Dependencies

- Decision on handling existing `check_manually=true` rows during migration (delete them in the migration — no audit trail needed)

### Open Questions

- None (resolved: no audit trail needed, just add/update datapoints; remove the Updated Raw Data Reports UI entirely)

### Assumptions

- The production config already has `CheckExistingRawReportUpdates: false`, so the manual review path is effectively unused in production
- Missing fields in a new scrape should NOT remove existing data (conservative merge: add/update only)
- The `instrument_events` mechanism is sufficient for triggering re-aggregation after merges
