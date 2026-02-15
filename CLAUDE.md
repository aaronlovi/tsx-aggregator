# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TSX stock market data aggregation platform. Scrapes Toronto Stock Exchange financial data, aggregates it, and serves it through a REST API to an Angular frontend.

## Tech Stack

- **Backend:** C# / .NET 10.0, ASP.NET Core, gRPC, PostgreSQL (Npgsql), PuppeteerSharp (web scraping), Serilog
- **Frontend:** Angular 16, TypeScript, Angular Material, RxJS
- **Testing:** xUnit, FluentAssertions, Moq (backend); Karma + Jasmine (frontend)
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
```

### Frontend (Angular)

```bash
cd deep-value
npm install
npm start          # Dev server at http://localhost:4200
npm test           # Karma + Jasmine tests
npm run build      # Production build
npm run build:ssr  # Server-side rendering build
```

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

### Frontend (deep-value/)

Angular 16 SPA with Angular Material. Key structure:
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
- `Ports.Grpc` -- gRPC listen port (default 7001)
- `HostedServices` -- Toggle individual background services
- `FeatureFlags` -- Feature toggles (e.g., `CheckExistingRawReportUpdates`)
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
