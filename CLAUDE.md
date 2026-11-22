# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TSX stock market data aggregation platform. Scrapes Toronto Stock Exchange financial data, aggregates it, and serves it through a REST API to an Angular frontend.

## Tech Stack

- **Backend:** C# / .NET 10.0, ASP.NET Core, gRPC, PostgreSQL (Npgsql), PuppeteerSharp (web scraping), Serilog
- **Frontend:** Angular 21, TypeScript, Angular Material, RxJS
- **Testing:** xUnit, FluentAssertions, Moq (backend); Vitest (frontend)
- **Infrastructure:** Docker Compose (PostgreSQL 17.4 + pgAdmin), Evolve (DB migrations)

## Build & Run Commands

### Backend (.NET)

```bash
# Build
cd src
dotnet build tsx-aggregator.sln

# Run tests
dotnet test tsx-aggregator.tests/

# Run a single test
dotnet test tsx-aggregator.tests/ --filter "FullyQualifiedName~TestMethodName"

# Run aggregator service (gRPC on port 7001)
dotnet run --project tsx-aggregator/

# Run REST API (ports 5000/5001)
dotnet run --project stock-market-webapi/

# Operations CLI (read-only, safe alongside the running services)
dotnet run --project tsx-ops -- help
dotnet run --project tsx-ops -- snapshot --label <name>     # score every company to snapshots/*.csv
dotnet run --project tsx-ops -- diff <a.csv> <b.csv>        # Markdown summary of score changes
dotnet run --project tsx-ops -- service status               # pausable services (needs tsx-aggregator running)
dotnet run --project tsx-ops -- service pause RawCollector --execute   # dry run without --execute
dotnet run --project tsx-ops -- events                       # Aggregator event queue: pending/stuck by type
dotnet run --project tsx-ops -- requeue --all --execute       # rebuild processed reports (dry run without --execute)
```

### Frontend (Angular)

```bash
cd deep-value
npm install
npm start          # Dev server at http://localhost:4200
npm test           # Vitest unit tests (ng test)
npm run build      # Production build
npm run build:ssr  # Server-side rendering build
```

### Containers (full stack)

```bash
cd docker-scripts
cp local.env.example local.env   # per-laptop settings and secrets (git-ignored)
docker compose --env-file .env --env-file local.env --profile app up -d --build tsx-web
```

Images: `src/tsx-aggregator/Dockerfile` (with Google Chrome), `src/stock-market-webapi/Dockerfile`, `deep-value/Dockerfile` (nginx; serves the app and forwards `/api/...` to the web API with the prefix removed, because the API's `/companies...` paths overlap the app's routes). Plain `docker compose up -d` still starts only PostgreSQL and pgAdmin. Deploy laptop setup: `deploy/README.md`.

### Database

```bash
cd docker-scripts
docker-compose up -d   # PostgreSQL on port 5433, pgAdmin on port 8081
```

Database migrations run automatically on service startup via Evolve. Migration files are in `src/dbm-persistence/Migrations/`.

## Build Configuration

Defined in `src/Directory.Build.props` and applied to all C# projects:
- `TreatWarningsAsErrors: true` -- all warnings are errors
- `Nullable: enable` -- nullable reference types enforced
- `ImplicitUsings: disable` -- all usings must be explicit
- Suppressed warnings: IDE0130, IDE0290

## Architecture

### Two-Process Backend

1. **tsx-aggregator** -- Worker service with multiple BackgroundServices and a gRPC server (port 7001)
2. **stock-market-webapi** -- ASP.NET Core REST API that acts as a gRPC client to tsx-aggregator

The web API is a thin translation layer: REST requests come in, get forwarded via gRPC to the aggregator, and responses are mapped back to HTTP/JSON.

### Background Services (tsx-aggregator)

Each can be toggled on/off via `HostedServices` in appsettings.json:

- **RawCollector** (`Raw/`) -- FSM-based service that scrapes TSX instrument directory and financial reports using PuppeteerSharp. Stores raw data in `instrument_reports` table.
- **Aggregator** (`Aggregated/`) -- FSM-based service that processes raw financial data into aggregated company reports (cash flow, owner earnings, book value). Writes to `processed_instrument_reports`.
- **QuoteService** (`QuotesService/`) -- Fetches stock prices from Google Sheets every 2 hours and caches them in memory.
- **SearchService** (`SearchService/`) -- Builds trie data structures from company names and symbols, rebuilt every 5 minutes. Returns up to 5 prefix-match results.
- **StocksDataRequestsProcessor** (`Services/`) -- Handles incoming gRPC requests.

### Shared Libraries

- **tsx-aggregator.models** -- DTOs, configuration option classes, report data models (CompanyReport, CashFlowItem, etc.)
- **tsx-aggregator.shared** -- Protobuf definitions (`Protos/`), trie data structure, utility classes, Result type
- **dbm-persistence** -- Database access layer with raw SQL via Npgsql, Evolve migrations

### Operations CLI (tsx-ops/)

Separate executable for operations-support work against a running system, with one subcommand per task (`snapshot`, `diff`, `service`, `events`, `requeue`). Add new operational tools as `tsx-ops` commands rather than new executables. Conventions:
- Read-only commands run all queries in one `REPEATABLE READ READ ONLY` transaction (`Db/OpsDb.cs`).
- Never use `DbmService`: its constructor runs migrations.
- Data goes to stdout or files, logs go to stderr. Exit codes: 0 ok, 1 error, 2 usage.
- Commands that change data must default to a dry run and require `--execute`.
- Changes to live service state go through tsx-aggregator's `AdminService` gRPC API (`admin.proto`, `Services/AdminSvc.cs`), not direct DB writes. Pausable services implement `IPausableService`.

### Frontend (deep-value/)

Angular 21 SPA with Angular Material. Key structure:
- `services/company.service.ts` -- Main data service calling the REST API
- `company-list/` -- Companies listing with sorting/filtering
- `company-details/` -- Individual company financial detail view
- `quick-search/` -- Prefix-based company search

### Data Flow

```
TSX Website --> PuppeteerSharp scraping --> PostgreSQL (raw reports)
                                              |
Google Sheets --> QuoteService (prices)       v
                                        Aggregator (processed reports)
                                              |
                                              v
                              gRPC service (tsx-aggregator:7001)
                                              |
                                              v
                              REST API (stock-market-webapi:5000)
                                              |
                                              v
                              Angular frontend (deep-value:4200)
```

### Key Configuration (appsettings.json)

- `ConnectionStrings.tsx-scraper` -- PostgreSQL connection (default port 5433)
- `Ports.Grpc` -- gRPC listen port (default 7001); `Ports.GrpcListenOnAllInterfaces` (default false: localhost only, since `AdminService` has no authentication)
- `Scraper.BrowserExecutablePath`, `Scraper.BrowserArgs` -- an installed Chrome for the RawCollector (container image); empty = Puppeteer downloads one at run time (dev)
- `Logs.WriteFile` (default true), `Logs.Directory` -- the daily `tsx-aggregator-*.txt` file; the container image turns it off (stdout only)
- `Api.ReadOnly` (stock-market-webapi, default false; true in the compose stack) -- 405 for anything but GET/HEAD/OPTIONS; `GET /health` for health checks
- `Aggregator.GrpcUrl` (stock-market-webapi) -- tsx-aggregator's gRPC address (default `http://localhost:7001`); `run.sh` sets it from `GRPC_PORT`
- `HostedServices` -- Toggle individual background services
- `FeatureFlags` -- Feature toggles (e.g., `CheckExistingRawReportUpdates`)
- `Scoring` -- Check 16 settings: `RoeConsistencyMinYears` (4), `RoeConsistencyWindowYears` (5), `RoeConsistencyMinUsableYears` (3); tsx-ops reads the same keys. Top-score cutoff is 14 of 16 (`ScoringOptions.TopScoreCutoff`)
- `GoogleCredentials` -- Google Sheets API config for price quotes

## Database

PostgreSQL with Evolve repeatable migrations (`R__*.sql` prefix). Core tables:
- `instruments` -- Stock instrument metadata
- `instrument_reports` -- Raw financial reports
- `instrument_prices` -- Historical stock prices
- `processed_instrument_reports` -- Aggregated financial data
- `raw_instrument_processing_state` -- RawCollector FSM state
- `state_fsm_state` -- Aggregator FSM state persistence

## License

All Rights Reserved. This source code is for reading and reference purposes only. Do not copy, modify, or distribute without explicit permission from the author (Aaron Lovi).
