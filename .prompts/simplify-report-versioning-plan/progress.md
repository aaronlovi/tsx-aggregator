# Progress

- [x] Checkpoint 1: Add merge infrastructure (additive, no breaking changes)
  - Files: RawReportDataMap.cs, RawFinancialsDelta.cs, UpdateInstrumentReportsStmt.cs, DbmInMemoryData.cs, DbmService.cs
  - Tests: RawReportDataMapMergeTests.cs (5 tests), RawFinancialsDeltaTests.cs (2 tests)
  - Committed: yes

- [x] Checkpoint 2: Switch RawFinancialDeltaTool to merge-override
  - Files: RawFinancialDeltaTool.cs, RawCollector.FetchInstrumentData.cs, RawFinancialDeltaToolTests.cs
  - Tests: TakeDelta_OneChangedProperty_ReturnsMergedUpdate, TakeDelta_NewFieldAdded_ReturnsMergedUpdate, TakeDelta_MissingFieldPreserved_ReturnsMergedUpdate
  - Committed: yes

- [x] Checkpoint 3: Remove feature flag and simplify DTOs and DB queries
  - Files: FeatureFlagsOptions.cs, DataTransferObjects.cs, RawReportConsistencyMap.cs, appsettings.json, appsettings.Development.json, DbmInMemoryData.cs, DbmInMemory.cs, DbmService.cs, IDbmService.cs, GetCurrentInstrumentReportsStmt.cs, GetRawFinancialsByInstrumentIdStmt.cs, GetInstrumentReportsStmt.cs, GetRawReportCountsByTypeStmt.cs, GetProcessedStockDataByExchangeStmt.cs, GetProcessedStockDataByExchangeAndInstrumentSymbolStmt.cs, UpdateInstrumentReportsStmt.cs, RawFinancialDeltaTool.cs, TestDataFactory.cs, RawFinancialDeltaToolTests.cs
  - Deleted: FeatureFlagsOptionsTests.cs
  - Tests: All 38 tests pass
  - Committed: yes

- [x] Checkpoint 4: Remove manual review gRPC, REST, and frontend
  - Files: IDbmService.cs, DbmService.cs, DbmInMemory.cs, DbmInMemoryData.cs, requests.proto, StockDataService.cs, RawCollector.cs, RawCollectorFsm.cs, RawCollectorInputs.cs, CompaniesController.cs, StocksDataRequestsProcessor.cs, GetDashboardStatsStmt.cs, DataTransferObjects.cs, DashboardStatsDto.cs, DashboardStatsResponse.cs, company.service.ts, dashboard-stats.ts, app-routing.module.ts, app.component.html, app.module.ts, dashboard.component.html
  - Deleted: GetRawInstrumentsWithUpdatedDataReportsStmt.cs, IgnoreRawDataReportStmt.cs, RawReportConsistencyMap.cs, InstrumentRawReportData.cs, InstrumentWithUpdatedRawData.cs, InstrumentsWithConflictingRawData.cs, updated-raw-data-reports/ (3 files), instrument_raw_report_data.ts, instrument_with_conflicting_raw_data.ts, instruments_with_conflicting_raw_data.ts, instrument_report_key.ts
  - Tests: DashboardStatsDtoTests.cs, DashboardStatsResponseTests.cs updated; all 38 tests pass
  - Committed: yes

- [x] Checkpoint 5: Database migration
  - Files: R__006__RemoveCheckManuallyAndIgnoreReport.sql (created)
  - Tests: No unit tests (pure SQL migration); all 38 existing tests pass
  - Committed: yes
