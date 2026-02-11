# Progress

- [x] Checkpoint 1: Database Layer — SQL Statement + DbmService Method
  - Files: GetDashboardStatsStmt.cs, GetRawReportCountsByTypeStmt.cs, DashboardStatsDto.cs, IDbmService.cs, DbmService.cs, DbmInMemory.cs, DbmInMemoryData.cs
  - Tests: DashboardStatsDtoTests.cs (4 tests)
  - Committed: yes

- [x] Checkpoint 2: gRPC Layer — Proto, Service, Processor
  - Files: requests.proto, StocksDataRequestsInputs.cs, StocksDataRequestsProcessor.cs, StockDataService.cs
  - Tests: none (wiring layer)
  - Committed: yes

- [x] Checkpoint 3: REST API Layer — Endpoint + DTO
  - Files: DashboardStatsResponse.cs, CompaniesController.cs
  - Tests: DashboardStatsResponseTests.cs (4 tests)
  - Committed: yes

- [x] Checkpoint 4: Angular Frontend — Dashboard Component
  - Files: dashboard-stats.ts, dashboard.component.ts, dashboard.component.html, dashboard.component.scss, dashboard.component.spec.ts, company.service.ts, app.module.ts, app-routing.module.ts, app.component.html
  - Tests: dashboard.component.spec.ts (4 tests)
  - Committed: yes
