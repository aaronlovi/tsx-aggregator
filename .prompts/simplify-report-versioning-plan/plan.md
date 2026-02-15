# Plan: Simplify Report Versioning to Merge-Override Model

## Overview

Replace the "keep both versions and let the user choose to accept or ignore" model with a simpler "new figures automatically merge into and override old figures" model. 5 checkpoints, progressing from additive infrastructure through behavioral change, cleanup, and migration.

## Checkpoint 1: Add merge infrastructure (additive, no breaking changes)

### Build

**1a. Add `MergeWith` method to `RawReportDataMap`** (`src/tsx-aggregator.models/RawReportDataMap.cs`)

Add a method that takes the existing report's JSON, starts with all its fields, overlays all keys from `this` (the new scraped report), and returns the merged JSON string:

```csharp
public string MergeWith(JsonDocument existingReportJson) {
    // Start with all existing fields (normalize keys to uppercase to match NormalizedStringKeysHashMap)
    var merged = new Dictionary<string, object>();
    foreach (JsonProperty prop in existingReportJson.RootElement.EnumerateObject()) {
        string normalizedKey = prop.Name.ToUpperInvariant();
        if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDecimal(out decimal val))
            merged[normalizedKey] = val;
        else if (prop.Value.ValueKind == JsonValueKind.String)
            merged[normalizedKey] = prop.Value.GetString()!;
    }
    // Overlay new numeric fields (add new keys, overwrite changed values)
    // Keys from NormalizedStringKeysHashMap are already uppercase
    foreach (string key in Keys) {
        merged[key] = this[key]!;
    }
    // Set REPORTDATE from this report if available
    if (ReportDate is not null)
        merged["REPORTDATE"] = ReportDate.Value.ToString("yyyy-MM-dd") + "T00:00:00Z";
    return JsonSerializer.Serialize(merged);
}
```

Key behavior: keys present only in the existing report are **preserved** (conservative merge). Keys in the new report add to or overwrite existing values. Non-numeric string fields (like REPORTDATE) in existing are preserved unless overwritten. Existing JSON keys are normalized to uppercase to match `NormalizedStringKeysHashMap` behavior and prevent key duplication.

**1b. Add `ReportUpdate` record** (`src/tsx-aggregator.models/RawFinancialsDelta.cs`)

```csharp
public record ReportUpdate(long InstrumentReportId, string MergedReportJson);
```

**1c. Add `InstrumentReportsToUpdate` to `RawFinancialsDelta`** (`src/tsx-aggregator.models/RawFinancialsDelta.cs`)

- Add `private readonly List<ReportUpdate> _instrumentReportsToUpdate;` field
- Initialize in constructors
- Add `public IList<ReportUpdate> InstrumentReportsToUpdate => _instrumentReportsToUpdate;`
- Copy in the copy constructor

**1d. Add UPDATE SQL path to `UpdateInstrumentReportsStmt`** (`src/dbm-persistence/Statements/UpdateInstrumentReportsStmt.cs`)

Add a new SQL constant:

```csharp
private const string UpdateReportJsonSql = "UPDATE instrument_reports"
    + " SET report_json = @report_json, created_date = @created_date"
    + " WHERE instrument_report_id = @instrument_report_id";
```

In the constructor, after the existing insert loop, add a loop over `InstrumentReportsToUpdate`:

```csharp
foreach (var reportUpdate in _rawFinancialsDelta.InstrumentReportsToUpdate) {
    AddCommandToBatch(UpdateReportJsonSql, new NpgsqlParameter[] {
        new NpgsqlParameter<string>("report_json", reportUpdate.MergedReportJson),
        new NpgsqlParameter<DateTime>("created_date", utcNow),
        new NpgsqlParameter<long>("instrument_report_id", reportUpdate.InstrumentReportId),
    });
}
```

Add a `NumReportsToUpdate` property. Update the event-firing condition (line 64) to also trigger when `NumReportsToUpdate > 0`:

```csharp
if (NumReportsToInsert > 0 || NumReportsToObsolete > 0 || NumReportsToUpdate > 0) {
```

**1e. Update `DbmInMemoryData`** (`src/dbm-persistence/DbmInMemoryData.cs`)

In the `UpdateInstrumentReports` method (line 512), after handling inserts (line 559) and before the event condition (line 562), add a loop to handle `InstrumentReportsToUpdate`: look up the list using `instrumentId` in `_rawDataReportsByInstrumentId`, find the matching entry by `InstrumentReportId`, and replace its `ReportJson` using record `with` syntax. Also update the event-firing condition (line 562) to trigger when there are updates:

```csharp
if (numReportsToInsert > 0 || instrumentReportsToObsolete.Count > 0
    || rawFinancialsDelta.InstrumentReportsToUpdate.Count > 0) {
```

**1f. Update `DbmService` logging** (`src/dbm-persistence/DbmService.cs`)

In `UpdateRawInstrumentReports` (line 160), update the success log message (line 164) to include the number of updated reports (`stmt.NumReportsToUpdate`).

### Test

**File: `src/tsx-aggregator.tests/RawReportDataMapMergeTests.cs`** (new file)

- `MergeWith_OverlaysNewFields`: existing `{A=1, B=2, C=3}`, new has `{B=9, D=4}` → merged `{A=1, B=9, C=3, D=4}`
- `MergeWith_PreservesExistingReportDate`: existing has `REPORTDATE` string field, new has no REPORTDATE → merged preserves the existing REPORTDATE
- `MergeWith_NewReportDateOverwritesExisting`: new has `ReportDate` set → merged uses new REPORTDATE
- `MergeWith_EmptyExisting`: existing is `{}`, new has `{A=1}` → merged is `{A=1}`
- `MergeWith_EmptyNew`: existing has `{A=1}`, new has no keys → merged is `{A=1}` (plus REPORTDATE if set)

**File: `src/tsx-aggregator.tests/RawFinancialsDeltaTests.cs`** (new file, or add to existing)

- Test that `RawFinancialsDelta` can hold items in all three lists simultaneously

### Verify

```
dotnet test src/tsx-aggregator.tests/
```

All existing tests still pass, new tests pass.

### Files

| Action | File |
|--------|------|
| Modify | `src/tsx-aggregator.models/RawReportDataMap.cs` |
| Modify | `src/tsx-aggregator.models/RawFinancialsDelta.cs` |
| Modify | `src/dbm-persistence/Statements/UpdateInstrumentReportsStmt.cs` |
| Modify | `src/dbm-persistence/DbmInMemoryData.cs` |
| Modify | `src/dbm-persistence/DbmService.cs` |
| Create | `src/tsx-aggregator.tests/RawReportDataMapMergeTests.cs` |

---

## Checkpoint 2: Switch RawFinancialDeltaTool to merge-override

### Build

**2a. Change `TakeDeltaCore` in `RawFinancialDeltaTool`** (`src/tsx-aggregator/Aggregated/RawFinancialDeltaTool.cs`)

Replace lines 105-116 (both branches of the `if (!_checkForReportChanges) { ... } else { ... }` block) with:

```csharp
// Merge new data into existing report (conservative: add/update only, preserve existing-only fields)
string mergedJson = newRawReport.MergeWith(existingReportJsonObj);
rawFinancialsDelta.InstrumentReportsToUpdate.Add(
    new ReportUpdate(existingReportDto.InstrumentReportId, mergedJson));
```

This reuses the `existingReportJsonObj` already parsed at line 100 (still in scope, not yet disposed). Do NOT add to `InstrumentReportsToInsert` or `InstrumentReportsToObsolete` for this case — the merge-and-update replaces both the old obsolete+insert path and the old manual-checking path. The `continue;` on line 116 is kept (now directly after this block).

**2b. Remove `_checkForReportChanges` field and feature flag usage from constructor**

Remove lines 17 and 22-23:

```csharp
// DELETE: private readonly bool _checkForReportChanges;
// DELETE: var featureFlags = svp.GetRequiredService<IOptions<FeatureFlagsOptions>>().Value;
// DELETE: _checkForReportChanges = featureFlags.CheckExistingRawReportUpdates.GetValueOrDefault();
```

Also remove the `using Microsoft.Extensions.Options;` import if no longer used.

**2c. Remove `AddReportForManualChecking` method** (lines 124-131)

Delete the entire method.

**2d. Remove `DropDuplicateCheckManuallyReports`** (`src/tsx-aggregator/Raw/RawCollector.FetchInstrumentData.cs`)

Delete the `DropDuplicateCheckManuallyReports()` method (lines 70-92) and its call on line 58.

### Test

**Update `RawFinancialDeltaToolTests`** (`src/tsx-aggregator.tests/RawFinancialDeltaToolTests.cs`):

- **Remove** the `[Theory] TakeDelta_OneChangedProperty_ReturnsExpectedDelta` test entirely (it tests the two feature flag branches which no longer exist).
- **Add** `TakeDelta_OneChangedProperty_ReturnsMergedUpdate`: existing has `{DATA_POINT=2}` for 2021, new has `{DATA_POINT=3}` for 2021 → delta has 0 inserts, 0 obsoletes, 1 update with merged JSON containing `DATA_POINT=3`.
- **Add** `TakeDelta_NewFieldAdded_ReturnsMergedUpdate`: existing has `{A=1}`, new has `{A=1, B=2}` → delta has 0 inserts, 0 obsoletes, 1 update with merged JSON `{A=1, B=2}`.
- **Add** `TakeDelta_MissingFieldPreserved_ReturnsMergedUpdate`: existing has `{A=1, B=2}`, new has `{A=1}` (B missing from new scrape) → delta has 0 inserts, 0 obsoletes, 1 update with merged JSON `{A=1, B=2}` (B preserved).
- **Update `SetupDI`**: remove the `checkExistingRawReportUpdates` parameter. The `FeatureFlagsOptions` registration should still provide a value (the property is still `[Required]` until CP3), so set it to any value — it's no longer read by `RawFinancialDeltaTool`.
- Existing `TakeDelta_NoReports_ReturnsExpectedDelta` and `TakeDelta_NoChanges_ReturnsNoDelta` should pass unchanged (also verify `InstrumentReportsToUpdate.Count == 0` in their assertions).

### Verify

```
dotnet test src/tsx-aggregator.tests/
```

### Files

| Action | File |
|--------|------|
| Modify | `src/tsx-aggregator/Aggregated/RawFinancialDeltaTool.cs` |
| Modify | `src/tsx-aggregator/Raw/RawCollector.FetchInstrumentData.cs` |
| Modify | `src/tsx-aggregator.tests/RawFinancialDeltaToolTests.cs` |

---

## Checkpoint 3: Remove feature flag and simplify DTOs and DB queries

### Build

**3a. Remove feature flag**

- Delete `CheckExistingRawReportUpdates` property from `FeatureFlagsOptions` (`src/tsx-aggregator.models/FeatureFlagsOptions.cs`). If this was the only property, the class becomes empty (keep the class shell — other code may reference the options pattern).
- Remove `"CheckExistingRawReportUpdates"` from `"FeatureFlags"` section in `appsettings.json` (`src/tsx-aggregator/appsettings.json`) and `appsettings.Development.json` (`src/tsx-aggregator/appsettings.Development.json`). If the `FeatureFlags` section becomes empty, remove the section entirely.

**3b. Remove `CheckManually` and `IgnoreReport` from `CurrentInstrumentRawDataReportDto`**

In `src/tsx-aggregator.models/DataTransferObjects.cs` (line 38-46), remove the last two parameters:

```csharp
// Before:
public record CurrentInstrumentRawDataReportDto(
    long InstrumentReportId, long InstrumentId, int ReportType,
    int ReportPeriodType, string ReportJson, DateOnly ReportDate,
    bool CheckManually, bool IgnoreReport);

// After:
public record CurrentInstrumentRawDataReportDto(
    long InstrumentReportId, long InstrumentId, int ReportType,
    int ReportPeriodType, string ReportJson, DateOnly ReportDate);
```

Fix all construction sites (compile errors will guide you):
- `GetCurrentInstrumentReportsStmt.ProcessCurrentRow` — remove `CheckManually: false, IgnoreReport: false` args
- `GetRawFinancialsByInstrumentIdStmt.ProcessCurrentRow` — remove `reader.GetBoolean(_checkManuallyIndex), IgnoreReport: false` args
- `RawFinancialDeltaTool.AddReportForInsertion` — remove `CheckManually: checkManually, IgnoreReport: false` args, and remove the `checkManually` parameter from the method signature
- `DbmInMemoryData` — all places that construct `CurrentInstrumentRawDataReportDto`
- `TestDataFactory.CreateCurrentInstrumentReportDto` — remove `checkManually` and `ignoreReport` parameters and their usage

**3c. Remove `CheckManually` and `IgnoreReport` from `InstrumentRawDataReportDto`**

In `src/tsx-aggregator.models/DataTransferObjects.cs` (line 396-407):

```csharp
// Before:
public record InstrumentRawDataReportDto(
    ..., bool IsCurrent, bool CheckManually, bool IgnoreReport);

// After:
public record InstrumentRawDataReportDto(
    ..., bool IsCurrent);
```

Fix construction sites:
- `GetInstrumentReportsStmt.ProcessCurrentRow` — remove `reader.GetBoolean(_checkManuallyIndex), reader.GetBoolean(_ignoreReportIndex)` args
- `DbmInMemoryData` — any methods that construct this DTO

**3d. Simplify SQL queries** — remove `check_manually` and `ignore_report` column references from:

| File | Change |
|------|--------|
| `GetCurrentInstrumentReportsStmt.cs` | Remove `AND check_manually = false AND ignore_report = false` from WHERE (lines 13-14) |
| `GetRawFinancialsByInstrumentIdStmt.cs` | Remove `AND ir.check_manually = false AND ir.ignore_report = false` from WHERE. Remove `ir.check_manually` from SELECT. Remove `_checkManuallyIndex` field and its usage in `BeforeRowProcessing` and `ProcessCurrentRow`. |
| `GetInstrumentReportsStmt.cs` | Remove `check_manually, ignore_report` from SELECT (lines 10-11). Remove `_checkManuallyIndex` and `_ignoreReportIndex` fields and all their usages. |
| `GetRawReportCountsByTypeStmt.cs` | Remove `AND ignore_report = false` from WHERE |
| `GetProcessedStockDataByExchangeStmt.cs` | Remove `AND ir.check_manually = FALSE AND ir.ignore_report = FALSE` from WHERE |
| `GetProcessedStockDataByExchangeAndInstrumentSymbolStmt.cs` | Remove `AND ir.check_manually = FALSE AND ir.ignore_report = FALSE` from WHERE |
| `UpdateInstrumentReportsStmt.cs` | Remove `check_manually` from `InsertSql` string and its parameter (line 57). Remove `NumReportsToCheckManually` property and tracking logic. |

Also update `DbmService.UpdateRawInstrumentReports` (line 164): remove `NumToCheckManually` from the log message (it references `stmt.NumReportsToCheckManually` which was removed above).

**3e. Remove unused methods and models**

- Remove `ExistsMatchingRawReport` from `IDbmService` (`src/dbm-persistence/IDbmService.cs`), `DbmService` (`src/dbm-persistence/DbmService.cs`), `DbmInMemory` (`src/dbm-persistence/DbmInMemory.cs`), `DbmInMemoryData` (`src/dbm-persistence/DbmInMemoryData.cs`).
- Delete `src/tsx-aggregator.models/RawReportConsistencyMap.cs`.
- Delete `src/tsx-aggregator.models/InstrumentRawReportData.cs`.

**3f. Clean up `DbmInMemoryData`** — remove all remaining `CheckManually` / `IgnoreReport` logic that no longer compiles or makes sense after the DTO changes.

### Test

- **Delete** `FeatureFlagsOptionsTests.cs` (`src/tsx-aggregator.tests/FeatureFlagsOptionsTests.cs`) entirely — the only property being validated was `CheckExistingRawReportUpdates`. If `FeatureFlagsOptions` still has other properties, update the tests instead.
- **Update** `TestDataFactory` — remove `DefaultCheckManually`, `DefaultIgnoreReport` constants and the `checkManually`/`ignoreReport` parameters.
- Fix any other compilation errors in tests.

### Verify

```
dotnet build src/tsx-aggregator.sln
dotnet test src/tsx-aggregator.tests/
```

### Files

| Action | File |
|--------|------|
| Modify | `src/tsx-aggregator.models/FeatureFlagsOptions.cs` |
| Modify | `src/tsx-aggregator/appsettings.json` |
| Modify | `src/tsx-aggregator/appsettings.Development.json` |
| Modify | `src/tsx-aggregator.models/DataTransferObjects.cs` |
| Modify | `src/dbm-persistence/Statements/GetCurrentInstrumentReportsStmt.cs` |
| Modify | `src/dbm-persistence/Statements/GetRawFinancialsByInstrumentIdStmt.cs` |
| Modify | `src/dbm-persistence/Statements/GetInstrumentReportsStmt.cs` |
| Modify | `src/dbm-persistence/Statements/GetRawReportCountsByTypeStmt.cs` |
| Modify | `src/dbm-persistence/Statements/GetProcessedStockDataByExchangeStmt.cs` |
| Modify | `src/dbm-persistence/Statements/GetProcessedStockDataByExchangeAndInstrumentSymbolStmt.cs` |
| Modify | `src/dbm-persistence/Statements/UpdateInstrumentReportsStmt.cs` |
| Modify | `src/tsx-aggregator/Aggregated/RawFinancialDeltaTool.cs` |
| Modify | `src/dbm-persistence/IDbmService.cs` |
| Modify | `src/dbm-persistence/DbmService.cs` |
| Modify | `src/dbm-persistence/DbmInMemory.cs` |
| Modify | `src/dbm-persistence/DbmInMemoryData.cs` |
| Modify | `src/tsx-aggregator.tests/TestDataFactory.cs` |
| Delete | `src/tsx-aggregator.tests/FeatureFlagsOptionsTests.cs` |
| Delete | `src/tsx-aggregator.models/RawReportConsistencyMap.cs` |
| Delete | `src/tsx-aggregator.models/InstrumentRawReportData.cs` |

---

## Checkpoint 4: Remove manual review gRPC, REST, and frontend

### Build (Backend)

**4a. Delete statement files**

- Delete `src/dbm-persistence/Statements/GetRawInstrumentsWithUpdatedDataReportsStmt.cs`
- Delete `src/dbm-persistence/Statements/IgnoreRawDataReportStmt.cs`

**4b. Remove DB service methods**

Remove from `IDbmService`, `DbmService`, `DbmInMemory`, and `DbmInMemoryData`:
- `GetRawInstrumentsWithUpdatedDataReports`
- `IgnoreRawUpdatedDataReport`

**4c. Remove DTOs used only by the removed workflow**

From `DataTransferObjects.cs`:
- Delete `RawInstrumentReportsToKeepAndIgnoreDto`
- Delete `PagedInstrumentsWithRawDataReportUpdatesDto`
- Delete any other DTOs only referenced by the removed code (let compile errors guide)

**4d. Remove gRPC definitions**

In `src/tsx-aggregator.shared/Protos/requests.proto`:
- Remove `rpc GetStocksWithUpdatedRawDataReports` and its request/response messages
- Remove `rpc IgnoreRawDataReport` and its request/response messages
- Remove `message InstrumentWithUpdatedRawDataItem`
- Remove `manual_review_count` field from `GetDashboardStatsReply`

**4e. Remove gRPC service implementations**

- Remove `GetStocksWithUpdatedRawDataReports()` and `IgnoreRawDataReport()` override methods from `StockDataService.cs` (`src/tsx-aggregator/Services/StockDataService.cs`)
- Remove `GetStocksWithUpdatedRawDataReports()` and `IgnoreRawDataReport()` methods from `RawCollector.cs` (`src/tsx-aggregator/Raw/RawCollector.cs`)

**4f. Remove REST endpoints**

In `CompaniesController.cs` (`src/stock-market-webapi/Controllers/CompaniesController.cs`):
- Remove `GetUpdatedRawDataReports()` endpoint
- Remove `IgnoreRawReport()` endpoint

**4g. Remove dashboard ManualReviewCount**

- `GetDashboardStatsStmt.cs` (`src/dbm-persistence/Statements/GetDashboardStatsStmt.cs`): remove the `manual_review_count` query and related code
- `DashboardStatsDto.cs` (`src/tsx-aggregator.models/DashboardStatsDto.cs`): remove `ManualReviewCount` property
- `DashboardStatsResponse.cs` (`src/tsx-aggregator.models/DashboardStatsResponse.cs`): remove `ManualReviewCount` property
- `StocksDataRequestsProcessor.cs` (`src/tsx-aggregator/Services/StocksDataRequestsProcessor.cs`): remove mapping of `manual_review_count`

### Build (Frontend)

**4h. Delete updated-raw-data-reports component**

- Delete entire `deep-value/src/app/updated-raw-data-reports/` directory

**4i. Delete frontend models**

- Delete `deep-value/src/app/models/instrument_raw_report_data.ts`
- Delete `deep-value/src/app/models/instrument_with_conflicting_raw_data.ts`
- Delete `deep-value/src/app/models/instruments_with_conflicting_raw_data.ts`
- Delete `deep-value/src/app/models/instrument_report_key.ts`

**4j. Remove service methods and dashboard references**

- In `company.service.ts` (`deep-value/src/app/services/company.service.ts`): remove `getUpdatedRawDataReports()` and `ignoreRawDataReports()` methods. Update the dashboard stats mapping to remove `manualReviewCount`.
- In `app-routing.module.ts` (`deep-value/src/app/app-routing.module.ts`): remove the route for updated-raw-data-reports.
- In `app.component.html` (`deep-value/src/app/app.component.html`): remove the nav link.
- In `dashboard.component.html` (`deep-value/src/app/dashboard/dashboard.component.html`): remove the `manualReviewCount` stat display.
- In `dashboard.component.ts`: remove any `manualReviewCount` references.
- In `dashboard-stats.ts` (`deep-value/src/app/models/dashboard-stats.ts`): remove `manualReviewCount` from the `DashboardStats` class and its constructor.

### Test

- Update `DashboardStatsDtoTests.cs` (`src/tsx-aggregator.tests/DashboardStatsDtoTests.cs`): remove `ManualReviewCount` references.
- Update `DashboardStatsResponseTests.cs` (`src/tsx-aggregator.tests/DashboardStatsResponseTests.cs`): remove `ManualReviewCount` references.
- Fix any other compile errors in tests.

### Verify

```
dotnet build src/tsx-aggregator.sln
dotnet test src/tsx-aggregator.tests/
cd deep-value && npm run build
```

### Files

| Action | File |
|--------|------|
| Delete | `src/dbm-persistence/Statements/GetRawInstrumentsWithUpdatedDataReportsStmt.cs` |
| Delete | `src/dbm-persistence/Statements/IgnoreRawDataReportStmt.cs` |
| Modify | `src/dbm-persistence/IDbmService.cs` |
| Modify | `src/dbm-persistence/DbmService.cs` |
| Modify | `src/dbm-persistence/DbmInMemory.cs` |
| Modify | `src/dbm-persistence/DbmInMemoryData.cs` |
| Modify | `src/tsx-aggregator.models/DataTransferObjects.cs` |
| Modify | `src/tsx-aggregator.shared/Protos/requests.proto` |
| Modify | `src/tsx-aggregator/Services/StockDataService.cs` |
| Modify | `src/tsx-aggregator/Raw/RawCollector.cs` |
| Modify | `src/stock-market-webapi/Controllers/CompaniesController.cs` |
| Modify | `src/dbm-persistence/Statements/GetDashboardStatsStmt.cs` |
| Modify | `src/tsx-aggregator.models/DashboardStatsDto.cs` |
| Modify | `src/tsx-aggregator.models/DashboardStatsResponse.cs` |
| Modify | `src/tsx-aggregator/Services/StocksDataRequestsProcessor.cs` |
| Modify | `src/tsx-aggregator.tests/DashboardStatsDtoTests.cs` |
| Modify | `src/tsx-aggregator.tests/DashboardStatsResponseTests.cs` |
| Delete | `deep-value/src/app/updated-raw-data-reports/` (entire directory) |
| Delete | `deep-value/src/app/models/instrument_raw_report_data.ts` |
| Delete | `deep-value/src/app/models/instrument_with_conflicting_raw_data.ts` |
| Delete | `deep-value/src/app/models/instruments_with_conflicting_raw_data.ts` |
| Delete | `deep-value/src/app/models/instrument_report_key.ts` |
| Modify | `deep-value/src/app/services/company.service.ts` |
| Modify | `deep-value/src/app/app-routing.module.ts` |
| Modify | `deep-value/src/app/app.component.html` |
| Modify | `deep-value/src/app/dashboard/dashboard.component.html` |
| Modify | `deep-value/src/app/dashboard/dashboard.component.ts` |
| Modify | `deep-value/src/app/models/dashboard-stats.ts` |

---

## Checkpoint 5: Database migration

### Build

Create `src/dbm-persistence/Migrations/R__006__RemoveCheckManuallyAndIgnoreReport.sql`:

```sql
-- Clean up orphaned rows from the old manual review system
DELETE FROM instrument_reports WHERE check_manually = true;
DELETE FROM instrument_reports WHERE ignore_report = true;

-- Drop the columns
ALTER TABLE instrument_reports DROP COLUMN IF EXISTS check_manually;
ALTER TABLE instrument_reports DROP COLUMN IF EXISTS ignore_report;
```

### Test

No unit tests for this checkpoint (pure SQL migration). Manual verification: start the tsx-aggregator service against the database, confirm the migration runs successfully and the service starts without errors. The `instrument_reports` table should no longer have `check_manually` or `ignore_report` columns.

### Verify

Start the tsx-aggregator service and confirm it starts without migration errors.

### Files

| Action | File |
|--------|------|
| Create | `src/dbm-persistence/Migrations/R__006__RemoveCheckManuallyAndIgnoreReport.sql` |

---

## Metadata

### Status

success

### Dependencies

- None (all decisions resolved in research phase)

### Open Questions

- None

### Assumptions

- Production config already has `CheckExistingRawReportUpdates: false`, so the manual review path is effectively unused
- Missing fields in a new scrape should NOT remove existing data (conservative merge: add/update only)
- The `instrument_events` mechanism is sufficient for triggering re-aggregation after merges
- Existing `check_manually=true` and `ignore_report=true` rows can be deleted in the migration (no audit trail needed)
- `DROP COLUMN IF EXISTS` is supported by the PostgreSQL version in use (PostgreSQL 17.4, confirmed)
