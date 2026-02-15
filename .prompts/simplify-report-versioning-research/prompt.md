# Research: Simplify Report Versioning to Merge-Override Model

## Objective

Map the current report versioning system end-to-end — database schema, backend logic, gRPC/REST layer, and frontend UI — so we can replace the "keep both versions and let the user choose to accept or ignore" model with a simpler "new figures automatically merge into and override old figures" model.

## Context

- Guidelines: `CLAUDE.md`
- Stack: C# .NET 8 backend (tsx-aggregator + stock-market-webapi), Angular frontend (deep-value/), PostgreSQL
- Data flow: Raw scraping -> `instrument_reports` table -> Aggregator processes -> `processed_instrument_reports` -> gRPC -> REST API -> Angular UI
- The "updated raw data reports" UI component is the frontend surface for the current accept/ignore versioning workflow

## Questions to Answer

### Database Schema & Migrations

1. Read `src/dbm-persistence/Migrations/R__003__AddCheckManuallyColumn.sql` and `R__005__AddIgnoreDataReportColumn.sql`. What columns do they add to which tables? What are the allowed values/types?
2. Read `src/dbm-persistence/Migrations/R__001__CreateTables.sql`. What is the full schema of `instrument_reports`? Which columns track versioning state (e.g., is there a `check_manually` flag, an `ignore` flag, a version number, or a separate row for the "updated" report)?
3. How are "original" vs "updated" report rows distinguished in `instrument_reports`? Is it a flag on the same row, or two rows linked by a foreign key?

### Database Access Layer

1. Read `src/dbm-persistence/Statements/GetRawInstrumentsWithUpdatedDataReportsStmt.cs`. What SQL query does it execute? What does "updated data reports" mean at the DB level — what WHERE clause identifies them?
2. Read `src/dbm-persistence/Statements/IgnoreRawDataReportStmt.cs`. What SQL does it execute? What column(s) does it update and to what value?
3. Read `src/dbm-persistence/Statements/UpdateInstrumentReportsStmt.cs`. What does this statement do — does it insert a new row or update in-place? Under what conditions is it called?
4. Read `src/dbm-persistence/Statements/GetCurrentInstrumentReportsStmt.cs` and `src/dbm-persistence/Statements/GetInstrumentReportsStmt.cs`. What is the difference between these two? Does "current" filter out updated/pending-review reports? What WHERE clauses distinguish them?
5. Check `src/dbm-persistence/IDbmService.cs` and `src/dbm-persistence/DbmService.cs` for all methods related to report versioning (search for methods that reference `CheckManually`, `IgnoreData`, `UpdatedReport`, `instrument_reports`). List them with their signatures.

### Backend - Report Comparison & Aggregation

1. Read `src/tsx-aggregator/Aggregated/RawFinancialDeltaTool.cs`. How does it compare old vs new report data? What is the input/output? Does it produce a diff, a merged result, or just a boolean "changed"?
2. Read `src/tsx-aggregator.tests/RawFinancialDeltaToolTests.cs`. What scenarios are tested? This reveals the expected behavior of the delta logic.
3. Read `src/tsx-aggregator.models/InstrumentRawReportData.cs` and `src/tsx-aggregator.models/RawReportConsistencyMap.cs`. What data structures represent a raw report and its consistency state?
4. Read `src/tsx-aggregator/Aggregated/Aggregator.cs`. When the Aggregator processes raw reports into processed reports, how does it handle the `check_manually` / `ignore_data_report` flags? Does it skip reports pending review, use the original, or use the updated version?

### Re-aggregation Trigger Mechanism

1. Read `src/dbm-persistence/Statements/InsertInstrumentEventStmt.cs`, `GetNextInstrumentEventStmt.cs`, and `UpdateInstrumentEventStmt.cs`. What is the `instrument_events` table schema? What event types exist? How does the Aggregator poll for and consume these events?
2. Read `src/dbm-persistence/Statements/InsertProcessedCompanyReportStmt.cs`. Does this INSERT or UPSERT into `processed_instrument_reports`? How does re-aggregation update an existing processed report?
3. Trace the full re-aggregation flow: when raw report data changes, what creates the event that triggers re-aggregation? Is it the RawCollector, the accept/ignore action, or something else? Follow the call chain from the trigger through to the updated `processed_instrument_reports` row.
4. Under the new merge-override model, when raw data is merged in-place, will the existing event mechanism automatically trigger re-aggregation, or does a new event need to be explicitly inserted?

### Backend - RawCollector (Scraping & Storage)

1. In `src/tsx-aggregator/Raw/RawCollector.FetchInstrumentData.cs` and `TsxCompanyProcessor.cs`, trace what happens when a newly scraped report differs from the existing one. Does it call `RawFinancialDeltaTool`? Does it set `check_manually`? Does it write a second row or update the existing one?
2. In `src/tsx-aggregator/Raw/RawCollector.cs`, is there any FSM state related to "pending manual review" reports?

### Backend - gRPC & REST Layer

1. In `src/tsx-aggregator.shared/Protos/requests.proto`, find all RPC methods and message types related to updated/versioned reports (search for `UpdatedReport`, `CheckManually`, `Ignore`, `Accept`).
2. In `src/tsx-aggregator/Services/StocksDataRequestsProcessor.cs`, trace how the gRPC service handles requests to list updated reports and to accept/ignore a report version.
3. In `src/stock-market-webapi/Controllers/CompaniesController.cs`, find the REST endpoints that correspond to the updated-reports workflow. What are their routes, HTTP methods, and request/response shapes?

### Frontend

1. Read `deep-value/src/app/updated-raw-data-reports/updated-raw-data-reports.ts` and its HTML template. What data does it display? What user actions does it support (buttons, links)? What service methods does it call?
2. In `deep-value/src/app/services/company.service.ts`, find all methods related to updated/versioned reports. What REST endpoints do they call?
3. Read `deep-value/src/app/models/instrument_raw_report_data.ts`. What fields does the frontend model have for versioning state?
4. Are there other frontend components that display or react to report version state? (Search for `CheckManually`, `updatedReport`, `ignore` in the `deep-value/src/` directory.)

## Explore

Specific files (read in this order):

1. `src/dbm-persistence/Migrations/R__001__CreateTables.sql`
2. `src/dbm-persistence/Migrations/R__003__AddCheckManuallyColumn.sql`
3. `src/dbm-persistence/Migrations/R__005__AddIgnoreDataReportColumn.sql`
4. `src/dbm-persistence/Statements/GetRawInstrumentsWithUpdatedDataReportsStmt.cs`
5. `src/dbm-persistence/Statements/IgnoreRawDataReportStmt.cs`
6. `src/dbm-persistence/Statements/UpdateInstrumentReportsStmt.cs`
7. `src/dbm-persistence/Statements/GetCurrentInstrumentReportsStmt.cs`
8. `src/dbm-persistence/Statements/GetInstrumentReportsStmt.cs`
9. `src/dbm-persistence/IDbmService.cs`
10. `src/tsx-aggregator.models/InstrumentRawReportData.cs`
11. `src/tsx-aggregator.models/RawReportConsistencyMap.cs`
12. `src/tsx-aggregator/Aggregated/RawFinancialDeltaTool.cs`
13. `src/tsx-aggregator.tests/RawFinancialDeltaToolTests.cs`
14. `src/tsx-aggregator/Aggregated/Aggregator.cs`
15. `src/dbm-persistence/Statements/InsertInstrumentEventStmt.cs`
16. `src/dbm-persistence/Statements/GetNextInstrumentEventStmt.cs`
17. `src/dbm-persistence/Statements/UpdateInstrumentEventStmt.cs`
18. `src/dbm-persistence/Statements/InsertProcessedCompanyReportStmt.cs`
19. `src/tsx-aggregator/Raw/RawCollector.FetchInstrumentData.cs`
20. `src/tsx-aggregator/Raw/TsxCompanyProcessor.cs`
21. `src/tsx-aggregator.shared/Protos/requests.proto`
22. `src/tsx-aggregator/Services/StocksDataRequestsProcessor.cs`
23. `src/stock-market-webapi/Controllers/CompaniesController.cs`
24. `deep-value/src/app/models/instrument_raw_report_data.ts`
25. `deep-value/src/app/services/company.service.ts`
26. `deep-value/src/app/updated-raw-data-reports/updated-raw-data-reports.ts`
27. `deep-value/src/app/updated-raw-data-reports/updated-raw-data-reports.html`

Search keywords: `check_manually`, `CheckManually`, `ignore_data_report`, `IgnoreData`, `RawFinancialDelta`, `ConsistencyMap`, `UpdatedReport`, `updated_report`, `manual review`, `manualReviewCount`, `instrument_event`, `InsertInstrumentEvent`, `unprocessed`, `processed_instrument_reports`, `InsertProcessedCompanyReport`

## Output

Write findings to `.prompts/simplify-report-versioning-research/research.md`:

- Answers to each numbered question above, organized by section
- A diagram or summary of the current version lifecycle (scrape -> detect diff -> store -> user reviews -> accept/ignore -> aggregator processes)
- List of all files that would need to change for the merge-override model
- Risks or concerns (e.g., data loss if fields disappear between scrapes, downstream effects on aggregator)
- Recommended approach for the merge-override model
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
