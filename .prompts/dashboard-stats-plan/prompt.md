# Plan: Dashboard Stats Page

## Context
- Research: `.prompts/dashboard-stats-research/research.md`
- Guidelines: `CLAUDE.md`

## Instructions
1. Read research.md for schema details, backend patterns, frontend patterns, and proposed SQL
2. Design implementation as checkpoints following the existing codebase patterns
3. Each checkpoint must include:
   - Build: what to implement
   - Test: what unit tests to write for THIS checkpoint's code
   - Verify: how to confirm all existing + new tests pass before moving on
4. NEVER design a dedicated "testing" checkpoint at the end. Tests are written alongside the code they verify, within the same checkpoint. Each checkpoint must leave the test suite green.

## Scope

### Dashboard Stats to Implement (v1)
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

### Layers to Build (bottom-up)
1. **Database** — New `GetDashboardStatsStmt` SQL statement class in `src/dbm-persistence/Statements/`
2. **DbmService** — New method on `IDbmService` / `DbmService` to execute the statement
3. **gRPC** — New RPC + messages in `requests.proto`, handler in `StockDataSvc.cs`, processor method in `StocksDataRequestsProcessor.cs`, request input class
4. **REST** — New endpoint in `CompaniesController.cs` (or new `DashboardController.cs`), new DTO
5. **Angular** — New `DashboardComponent`, model class, service method, route, nav link, `MatCardModule` import

### Key Patterns to Follow (from research)
- SQL statements: extend `QueryDbStmtBase`, cache column ordinals, use parameterized queries
- DbmService: return `ValueTask<(Result, T)>`, use `_exec.ExecuteWithRetry()`
- gRPC: define in `requests.proto`, implement in `StockDataSvc.cs`, process in `StocksDataRequestsProcessor.cs`
- REST: call gRPC client, transform to DTO, return `Ok()` or `BadRequest()`
- Angular: component with `loading`/`errorMsg` state, service call in `ngOnInit()`, Material card layout

### Key Files to Reference
- Statement pattern: `src/dbm-persistence/Statements/GetInstrumentListStmt.cs`
- Base classes: `src/dbm-persistence/Statements/QueryDbStmtBase.cs`
- DbmService interface: `src/dbm-persistence/IDbmService.cs`
- DbmService impl: `src/dbm-persistence/DbmService.cs`
- Proto: `src/tsx-aggregator.shared/Protos/requests.proto`
- gRPC service: `src/tsx-aggregator/Services/StockDataSvc.cs`
- Processor: `src/tsx-aggregator/Services/StocksDataRequestsProcessor.cs`
- Request inputs: `src/tsx-aggregator/Services/StocksDataRequestsInputs.cs`
- REST controller: `src/stock-market-webapi/Controllers/CompaniesController.cs`
- Angular module: `deep-value/src/app/app.module.ts`
- Angular routing: `deep-value/src/app/app-routing.module.ts`
- Angular nav: `deep-value/src/app/app.component.html`
- Company service: `deep-value/src/app/services/company.service.ts`

### Build Notes
- Build individual projects, not the full solution: `dotnet build tsx-aggregator/tsx-aggregator.csproj` or `dotnet build stock-market.webapi.csproj`
- Angular: `cd deep-value && npm run build`
- Backend tests: `dotnet test tsx-aggregator.tests/`
- TreatWarningsAsErrors is enabled — all warnings are errors
- Nullable reference types enforced
- ImplicitUsings disabled — all usings must be explicit

## Output
Write plan to `.prompts/dashboard-stats-plan/plan.md`:
- Ordered checkpoints (implementation + tests each — no checkpoint without tests unless it is purely non-code work like documentation or configuration)
- Files to create/modify
- Metadata block (Status, Dependencies, Open Questions, Assumptions)
