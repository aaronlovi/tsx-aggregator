# Research: Dashboard Stats Page

## Objective
Understand the database schema, existing data access patterns, and frontend architecture well enough to design a dashboard page showing aggregated statistics about the underlying data (e.g., instrument counts, report coverage, processing status, score distributions).

## Context
- Guidelines: `CLAUDE.md`
- Database migrations: `src/dbm-persistence/Migrations/R__001__CreateTables.sql`
- Database access layer: `src/dbm-persistence/` (raw SQL via Npgsql)
- Existing SQL statements: `src/dbm-persistence/Statements/`
- gRPC service: `src/tsx-aggregator/Services/StocksDataRequestsProcessor.cs`
- Proto definitions: `src/tsx-aggregator.shared/Protos/`
- REST API controller: `src/stock-market-webapi/Controllers/CompaniesController.cs`
- Web API program/startup: `src/stock-market-webapi/Program.cs`
- Angular services: `deep-value/src/app/services/company.service.ts`
- Angular routing: `deep-value/src/app/app-routing.module.ts`
- Angular app module: `deep-value/src/app/app.module.ts`
- Angular nav menu: `deep-value/src/app/app.component.html`
- Stack: .NET 8 / PostgreSQL / gRPC / Angular 16

## Questions to Answer

### Database & Data
1. What tables exist and what columns do they have? (Full schema from migration files)
2. What useful aggregate stats can be derived? Consider:
   - Total number of instruments
   - Number of instruments with processed reports vs without
   - Number of raw reports vs processed reports
   - Date of most recent raw report ingestion
   - Date of most recent aggregation run
   - Distribution of overall scores (how many score 13, 12, 11, etc.)
   - Average/median market cap
   - Number of instruments by exchange
   - Any FSM state info (how many instruments pending, in-progress, completed)
3. Which of these queries would be fast (indexed lookups, small result sets) vs slow (full table scans, aggregations over large datasets)?
4. For slow queries: is there an existing pattern for scheduled/cached data, or would we need to introduce one?

### Backend Data Access
5. What patterns do existing `Statements/` classes follow? (naming, structure, how they execute SQL, how results are returned)
6. How are new gRPC endpoints added? What's the pattern in the proto files and `StocksDataRequestsProcessor`?
7. How are new REST endpoints added in `CompaniesController`? What's the mapping pattern from gRPC reply to DTO?

### Frontend
8. What's the pattern for adding a new page/route in the Angular app? (module registration, routing, nav menu entry)
9. What Angular Material components are used for data display besides tables? (cards, lists, chips, etc. — what's already imported?)
10. Is there an existing dashboard or summary-style component to use as a reference?

### Architecture Decision: Real-time vs Cached
11. Given the data volumes, should stats be computed on-demand per request, or pre-computed on a schedule?
12. If scheduled: is there an existing background service pattern that could run a daily stats computation? (Look at existing BackgroundService implementations)
13. Where would cached stats be stored — in a new DB table, or in-memory in the aggregator service?

## Explore
- `src/dbm-persistence/Migrations/` — full schema definitions
- `src/dbm-persistence/Statements/` — all existing SQL statement classes for patterns
- `src/dbm-persistence/DbmContext.cs` or similar — DB connection/context patterns
- `src/tsx-aggregator.shared/Protos/` — existing proto definitions
- `src/tsx-aggregator/Services/StocksDataRequestsProcessor.cs` — gRPC request handling
- `src/stock-market-webapi/Controllers/` — REST controller patterns
- `src/tsx-aggregator/Aggregated/` — background service pattern (for scheduled computation)
- `src/tsx-aggregator/Raw/` — another background service pattern
- `deep-value/src/app/app.module.ts` — imported Material modules
- `deep-value/src/app/app-routing.module.ts` — routing patterns
- `deep-value/src/app/app.component.html` — nav menu structure

## Output
Write findings to `.prompts/dashboard-stats-research/research.md`:
- Answers to the questions above
- Existing patterns to follow
- Risks or concerns
- Recommended approach (real-time vs cached, proposed stats list, suggested SQL)
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
