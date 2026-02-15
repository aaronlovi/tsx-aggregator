# Plan: Simplify Report Versioning to Merge-Override Model

## Context

- Research: `.prompts/simplify-report-versioning-research/research.md`
- Guidelines: `CLAUDE.md`
- Decisions already made:
  - No audit trail needed -- just add/update datapoints silently
  - Remove the "Updated Raw Data Reports" UI entirely (do not repurpose)
  - Missing fields in a new scrape should NOT remove existing data (conservative merge: add/update only)

## Goal

Replace the current "keep both versions and let the user choose to accept or ignore" model with a simpler "new figures automatically merge into and override old figures" model. When a newly scraped report differs from the existing one for the same (instrument, report_type, report_period_type, report_date), merge the new fields into the existing row's `report_json` in-place (UPDATE, not obsolete+insert). New keys are added, changed values are overwritten, and keys present only in the existing report are preserved.

## Instructions

1. Read `.prompts/simplify-report-versioning-research/research.md`
2. Design implementation as checkpoints (see structure below)
3. Each checkpoint must include:
   - **Build**: what to implement
   - **Test**: what unit tests to write for THIS checkpoint's code
   - **Verify**: how to confirm all existing + new tests pass before moving on
4. NEVER design a dedicated "testing" checkpoint at the end. Tests are written alongside the code they verify, within the same checkpoint. Each checkpoint must leave the test suite green.

## Checkpoint Structure

The checkpoints should follow this general progression. Adjust as needed based on the research findings, but maintain the principle that each checkpoint is independently compilable and all tests pass after each one.

### Checkpoint 1: Add merge infrastructure (additive, no breaking changes)

**Build:**

- Add a `MergeWith(JsonDocument existingReportJson)` method (or similar) to `RawReportDataMap` (`src/tsx-aggregator.models/RawReportDataMap.cs`) that returns a merged JSON string. Logic: parse existing JSON, overlay all keys from `this` (the new report) onto it, return serialized result. Keys in existing but not in new are preserved. Keys in new overwrite existing.
- Add a `ReportUpdate` record to `src/tsx-aggregator.models/` (e.g., in `DataTransferObjects.cs` or `RawFinancialsDelta.cs`): `record ReportUpdate(long InstrumentReportId, string MergedReportJson)` -- represents an in-place update to an existing report row.
- Add `IList<ReportUpdate> InstrumentReportsToUpdate` to `RawFinancialsDelta` (`src/tsx-aggregator.models/RawFinancialsDelta.cs`).
- Add UPDATE SQL path to `UpdateInstrumentReportsStmt` (`src/dbm-persistence/Statements/UpdateInstrumentReportsStmt.cs`): for each item in `InstrumentReportsToUpdate`, emit `UPDATE instrument_reports SET report_json = @report_json, created_date = @created_date WHERE instrument_report_id = @instrument_report_id`. Include these updates in the condition that fires `instrument_event(RawDataChanged)`.
- Update `DbmInMemoryData` (`src/dbm-persistence/DbmInMemoryData.cs`) to handle the new `InstrumentReportsToUpdate` list in its `UpdateRawInstrumentReports` method.

**Test:**

- Unit test for `RawReportDataMap.MergeWith`: existing has keys {A, B, C}, new has {B, D} with B changed -- merged result has {A, B(new), C, D}. Also test that non-numeric fields (like REPORTDATE) in existing are preserved.
- Test that `RawFinancialsDelta` can hold items in all three lists (insert, obsolete, update).

**Verify:** `dotnet test src/tsx-aggregator.tests/` -- all existing tests still pass, new tests pass.

### Checkpoint 2: Switch RawFinancialDeltaTool to merge-override

**Build:**

- In `RawFinancialDeltaTool.TakeDeltaCore` (`src/tsx-aggregator/Aggregated/RawFinancialDeltaTool.cs`), when an existing report differs from the new scrape:
  - Call the merge method to produce merged JSON
  - Add a `ReportUpdate(existingReportDto.InstrumentReportId, mergedJson)` to `rawFinancialsDelta.InstrumentReportsToUpdate`
  - Do NOT add to `InstrumentReportsToInsert` or `InstrumentReportsToObsolete` for this case
- Remove the `_checkForReportChanges` field and the `AddReportForManualChecking` method. The `else` branch (lines 109-113) is deleted.
- Remove `DropDuplicateCheckManuallyReports()` method and its call from `RawCollector.FetchInstrumentData.cs` (`src/tsx-aggregator/Raw/RawCollector.FetchInstrumentData.cs`).
- The `_dbm` and `IDbmService` dependency in `RawFinancialDeltaTool` constructor was only needed for `GetNextId64` (in `AddReportForInsertion`). Verify whether `_dbm` is still needed after this change -- it is, because `AddReportForInsertion` still calls `_dbm.GetNextId64` for genuinely new reports (no matching existing row).

**Test:**

- Update `RawFinancialDeltaToolTests` (`src/tsx-aggregator.tests/RawFinancialDeltaToolTests.cs`):
  - Remove or rework the `[InlineData(true, ...)]` test case (the `CheckExistingRawReportUpdates=true` path no longer exists).
  - Add test: when existing report has {A=1, B=2} and new has {B=3, C=4}, delta has 0 inserts, 0 obsoletes, 1 update with merged JSON {A=1, B=3, C=4}.
  - Existing test for "same data -> no delta" should still pass unchanged.
  - Existing test for "no reports -> empty delta" should still pass unchanged.

**Verify:** `dotnet test src/tsx-aggregator.tests/` -- all tests pass.

### Checkpoint 3: Remove feature flag and simplify DTOs and DB queries

**Build:**

- Remove `CheckExistingRawReportUpdates` from `FeatureFlagsOptions` (`src/tsx-aggregator.models/FeatureFlagsOptions.cs`), `appsettings.json` (`src/tsx-aggregator/appsettings.json`), and `appsettings.Development.json` (`src/tsx-aggregator/appsettings.Development.json`).
- Remove `CheckManually` and `IgnoreReport` parameters from `CurrentInstrumentRawDataReportDto` (`src/tsx-aggregator.models/DataTransferObjects.cs`). Fix all construction sites:
  - `GetCurrentInstrumentReportsStmt.ProcessCurrentRow` (`src/dbm-persistence/Statements/GetCurrentInstrumentReportsStmt.cs`)
  - `RawFinancialDeltaTool.AddReportForInsertion` (`src/tsx-aggregator/Aggregated/RawFinancialDeltaTool.cs`)
  - `DbmInMemoryData` (`src/dbm-persistence/DbmInMemoryData.cs`)
  - `TestDataFactory` (`src/tsx-aggregator.tests/TestDataFactory.cs`)
  - Any other construction sites found by compile errors
- Remove `CheckManually` and `IgnoreReport` from `InstrumentRawDataReportDto` (`src/tsx-aggregator.models/DataTransferObjects.cs` line 406-407). Fix all construction sites:
  - `GetInstrumentReportsStmt.ProcessCurrentRow` (`src/dbm-persistence/Statements/GetInstrumentReportsStmt.cs`)
  - `DbmInMemoryData` -- any methods that construct this DTO
- Simplify `GetCurrentInstrumentReportsStmt` SQL: remove `AND check_manually = false AND ignore_report = false` from the WHERE clause (lines 13-14).
- Simplify `GetRawFinancialsByInstrumentIdStmt` (`src/dbm-persistence/Statements/GetRawFinancialsByInstrumentIdStmt.cs`) SQL: remove `AND ir.check_manually = false AND ir.ignore_report = false` from WHERE clause. Remove `ir.check_manually` from SELECT list. Remove `_checkManuallyIndex` field and its usage. Fix `CurrentInstrumentRawDataReportDto` construction in `ProcessCurrentRow`.
- Simplify `GetInstrumentReportsStmt` SQL: remove `check_manually` and `ignore_report` from SELECT list (line 10-11). Remove `_checkManuallyIndex` and `_ignoreReportIndex` fields and their usages.
- Simplify `GetRawReportCountsByTypeStmt` (`src/dbm-persistence/Statements/GetRawReportCountsByTypeStmt.cs`) SQL: remove `AND ignore_report = false` from WHERE clause.
- Simplify `GetProcessedStockDataByExchangeStmt` (`src/dbm-persistence/Statements/GetProcessedStockDataByExchangeStmt.cs`) SQL: remove `AND ir.check_manually = FALSE AND ir.ignore_report = FALSE` from WHERE clause.
- Simplify `GetProcessedStockDataByExchangeAndInstrumentSymbolStmt` (`src/dbm-persistence/Statements/GetProcessedStockDataByExchangeAndInstrumentSymbolStmt.cs`) SQL: remove `AND ir.check_manually = FALSE AND ir.ignore_report = FALSE` from WHERE clause.
- Remove `check_manually` from `InsertSql` in `UpdateInstrumentReportsStmt` (line 12, and the parameter at line 57). Remove `NumReportsToCheckManually` property and its tracking logic.
- Remove `ExistsMatchingRawReport` from `IDbmService` and `DbmService`.
- Remove the `checkManually` parameter from `AddReportForInsertion` in `RawFinancialDeltaTool`.
- Delete `src/tsx-aggregator.models/RawReportConsistencyMap.cs`.
- Delete `src/tsx-aggregator.models/InstrumentRawReportData.cs`.

**Test:**

- Update `FeatureFlagsOptionsTests` (`src/tsx-aggregator.tests/FeatureFlagsOptionsTests.cs`): remove test cases referencing `CheckExistingRawReportUpdates`.
- Fix any compilation errors in existing tests caused by DTO changes.

**Verify:** `dotnet test src/tsx-aggregator.tests/` -- all tests pass. Also `dotnet build src/tsx-aggregator.sln` to catch compile errors across all projects.

### Checkpoint 4: Remove manual review gRPC, REST, and frontend

**Build (Backend):**

- Delete `src/dbm-persistence/Statements/GetRawInstrumentsWithUpdatedDataReportsStmt.cs`.
- Delete `src/dbm-persistence/Statements/IgnoreRawDataReportStmt.cs`.
- Remove `GetRawInstrumentsWithUpdatedDataReports` and `IgnoreRawUpdatedDataReport` from `IDbmService` (`src/dbm-persistence/IDbmService.cs`) and `DbmService` (`src/dbm-persistence/DbmService.cs`) and `DbmInMemory` (`src/dbm-persistence/DbmInMemory.cs`) and `DbmInMemoryData` (`src/dbm-persistence/DbmInMemoryData.cs`).
- Remove `GetStocksWithUpdatedRawDataReports` and `IgnoreRawDataReport` RPC methods and associated request/response/item messages from `requests.proto` (`src/tsx-aggregator.shared/Protos/requests.proto`).
- Remove `GetStocksWithUpdatedRawDataReports()` and `IgnoreRawDataReport()` override methods from `StockDataService.cs` (`src/tsx-aggregator/Services/StockDataService.cs`).
- Remove the `GetStocksWithUpdatedRawDataReports()` and `IgnoreRawDataReport()` methods from `RawCollector.cs` (`src/tsx-aggregator/Raw/RawCollector.cs`).
- Remove `GetUpdatedRawDataReports()` and `IgnoreRawReport()` endpoints from `CompaniesController.cs` (`src/stock-market-webapi/Controllers/CompaniesController.cs`).
- Remove `RawInstrumentReportsToKeepAndIgnoreDto` and `PagedInstrumentsWithRawDataReportUpdatesDto` and any other DTOs only used by the removed workflow from `DataTransferObjects.cs`.
- Remove `ManualReviewCount` from dashboard: `GetDashboardStatsStmt.cs`, `DashboardStatsDto.cs`, `DashboardStatsResponse.cs`, `requests.proto` (`manual_review_count` in `GetDashboardStatsReply`), and any mapping code in `StocksDataRequestsProcessor.cs`.

**Build (Frontend):**

- Delete the entire `deep-value/src/app/updated-raw-data-reports/` directory.
- Delete `deep-value/src/app/models/instrument_raw_report_data.ts`, `instrument_with_conflicting_raw_data.ts`, `instruments_with_conflicting_raw_data.ts`, `instrument_report_key.ts`.
- Remove `getUpdatedRawDataReports()` and `ignoreRawDataReports()` from `company.service.ts` (`deep-value/src/app/services/company.service.ts`). Also update the dashboard stats mapping in the same file to remove `manualReviewCount`.
- Remove the route from `app-routing.module.ts` (`deep-value/src/app/app-routing.module.ts`).
- Remove the nav link from `app.component.html` (`deep-value/src/app/app.component.html`).
- Remove `manualReviewCount` display from the dashboard template (`deep-value/src/app/dashboard/dashboard.component.html`) and its component TypeScript file.
- Remove `manualReviewCount` from `DashboardStats` model (`deep-value/src/app/models/dashboard-stats.ts`).

**Test:**

- Update `DashboardStatsDtoTests.cs` and `DashboardStatsResponseTests.cs` to remove `ManualReviewCount` references.
- Fix any other compile errors in tests.
- Run `npm run build` in `deep-value/` to verify Angular compiles.

**Verify:** `dotnet test src/tsx-aggregator.tests/` and `dotnet build src/tsx-aggregator.sln` and `cd deep-value && npm run build`.

### Checkpoint 5: Database migration

**Build:**

- Create `src/dbm-persistence/Migrations/R__006__RemoveCheckManuallyAndIgnoreReport.sql`:
  - Delete any rows with `check_manually = true` (orphaned pending-review rows).
  - Delete any rows with `ignore_report = true` (already-ignored rows).
  - `ALTER TABLE instrument_reports DROP COLUMN check_manually;`
  - `ALTER TABLE instrument_reports DROP COLUMN ignore_report;`

**Test:**

- No unit tests for this checkpoint (pure SQL migration).
- Manual verification: start the service against the database, confirm the migration runs successfully and the service starts without errors.

**Verify:** Start the tsx-aggregator service and confirm it starts successfully with no migration errors. The `instrument_reports` table should no longer have `check_manually` or `ignore_report` columns.

## Output

Write plan to `.prompts/simplify-report-versioning-plan/plan.md`:

- Ordered checkpoints as above (implementation + tests each)
- Files to create/modify per checkpoint
- Metadata block (Status, Dependencies, Open Questions, Assumptions)
