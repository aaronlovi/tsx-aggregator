# Plan: Override Raw Collector Instrument Priority

## Overview

Add the ability to override which companies the RawCollector fetches next by posting a priority list of company symbols. The priority queue is in-memory (transient), drains before resuming normal round-robin, and triggers an immediate fetch. Includes backend (Registry, FSM, gRPC, REST) and frontend (Angular "System Controls" page).

## Files Summary

### New files

- `src/tsx-aggregator/Raw/RawCollectorInputs.Priority.cs` — new input message types
- `src/tsx-aggregator.tests/RegistryPriorityTests.cs` — Registry priority queue tests
- `src/tsx-aggregator.tests/RawCollectorFsmPriorityTests.cs` — FSM priority override tests
- `deep-value/src/app/system-controls/system-controls.component.ts`
- `deep-value/src/app/system-controls/system-controls.component.html`
- `deep-value/src/app/system-controls/system-controls.component.scss`

### Modified files

- `src/tsx-aggregator/Raw/Registry.cs` — priority queue methods
- `src/tsx-aggregator/Raw/RawCollectorFsm.cs` — check priority before round-robin
- `src/tsx-aggregator/Raw/RawCollector.cs` — handle new input types in PreprocessInputs
- `src/tsx-aggregator.shared/Protos/requests.proto` — new gRPC messages and RPCs
- `src/tsx-aggregator/Services/StockDataService.cs` — new gRPC handlers
- `src/stock-market-webapi/Controllers/CompaniesController.cs` — new REST endpoints
- `deep-value/src/app/app.module.ts` — declare new component
- `deep-value/src/app/app-routing.module.ts` — add route
- `deep-value/src/app/app.component.html` — add sidebar nav item
- `deep-value/src/app/services/company.service.ts` — add service methods
- `deep-value/src/app/services/text.service.ts` — add i18n keys
- `deep-value/src/assets/i18n/en-US.json` — English translations
- `deep-value/src/assets/i18n/zh-CN.json` — Chinese translations

---

## Checkpoints

### 1. Registry priority queue

**Build:**

- Add a `Queue<string> _priorityCompanySymbols` field to `Registry` (with a dedicated `object _priorityLock` for thread safety).
- Add methods:
  - `SetPriorityCompanies(IReadOnlyList<string> companySymbols)` — clears existing queue, enqueues new symbols (deduped), returns count of valid symbols found in the instrument list.
  - `TryDequeueNextPriorityInstrumentKey(out InstrumentKey? key)` — dequeues the next symbol, resolves it to the first matching `InstrumentKey` by iterating `_instruments` for a CompanySymbol match. Skips unknown/missing symbols (logs warning). Returns `true` if a key was found, `false` if queue is empty or all remaining symbols were invalid.
  - `GetPriorityCompanySymbols()` — returns `IReadOnlyList<string>` snapshot of the current queue contents (without dequeuing).
  - `ClearPriorityCompanies()` — clears the queue.

**Test:** `src/tsx-aggregator.tests/RegistryPriorityTests.cs`

- Test `SetPriorityCompanies` populates queue, dedup works
- Test `TryDequeueNextPriorityInstrumentKey` returns correct InstrumentKey for known symbol
- Test `TryDequeueNextPriorityInstrumentKey` skips unknown symbols
- Test `TryDequeueNextPriorityInstrumentKey` returns false when queue is empty
- Test `GetPriorityCompanySymbols` returns snapshot without consuming
- Test `ClearPriorityCompanies` empties the queue
- Test setting new priority replaces old queue

**Verify:** `dotnet test src/tsx-aggregator.tests/`

---

### 2. FSM priority override + input messages

**Build:**

- Modify `RawCollectorFsm.ProcessUpdateTime()`: Before `_registry.GetNextInstrumentKey(PrevInstrumentKey)`, call `_registry.TryDequeueNextPriorityInstrumentKey(out var priorityKey)`. If it returns true, use `priorityKey` instead of the round-robin key. Do NOT update `PrevInstrumentKey` with the priority key (so round-robin resumes from where it left off).
- Add new input message types in `src/tsx-aggregator/Raw/RawCollectorInputs.Priority.cs`:
  - `RawCollectorSetPriorityCompaniesInput` — carries `IReadOnlyList<string> CompanySymbols`
  - `RawCollectorGetPriorityCompaniesInput` — no extra data, response returns the current queue
- Handle in `RawCollector.PreprocessInputs()`:
  - `RawCollectorSetPriorityCompaniesInput`: call `_registry.SetPriorityCompanies(...)`, set `_stateFsm.NextFetchInstrumentDataTime = DateTime.UtcNow` to trigger immediate fetch, set result on `Completed`.
  - `RawCollectorGetPriorityCompaniesInput`: call `_registry.GetPriorityCompanySymbols()`, set result on `Completed`.
- Add these new input types to the FSM `Update` switch statement (they should fall through to `ProcessUpdateTime` like `RawCollectorIgnoreRawReportInput` does).

**Test:** `src/tsx-aggregator.tests/RawCollectorFsmPriorityTests.cs`

- Test that when priority queue has items, `ProcessUpdateTime` produces a `FetchRawCollectorInstrumentDataOutput` with the priority company's instrument data (not the round-robin next)
- Test that `PrevInstrumentKey` is NOT updated when a priority key is used
- Test that when priority queue is empty, normal round-robin resumes
- Test that when priority queue has an unknown symbol, it is skipped and the next valid one is used

**Verify:** `dotnet test src/tsx-aggregator.tests/`

---

### 3. gRPC proto + service handlers

**Build:**

- Add to `src/tsx-aggregator.shared/Protos/requests.proto`:

  ```protobuf
  rpc SetPriorityCompanies(SetPriorityCompaniesRequest) returns (StockDataServiceReply);
  rpc GetPriorityCompanies(GetPriorityCompaniesRequest) returns (GetPriorityCompaniesReply);

  message SetPriorityCompaniesRequest {
      repeated string company_symbols = 1;
  }

  message GetPriorityCompaniesRequest { }

  message GetPriorityCompaniesReply {
      bool success = 1;
      string error_message = 2;
      repeated string company_symbols = 3;
  }
  ```

- Implement `SetPriorityCompanies` in `StockDataSvc`: create `RawCollectorSetPriorityCompaniesInput`, post to `_rawCollector.PostRequest()`, await `Completed.Task`, return success/failure.
- Implement `GetPriorityCompanies` in `StockDataSvc`: create `RawCollectorGetPriorityCompaniesInput`, post to `_rawCollector.PostRequest()`, await `Completed.Task`, return the list.

**Test:** No new unit tests for this checkpoint — the gRPC handlers are thin plumbing that delegate to the RawCollector (already tested in checkpoint 2). The proto compilation itself validates correctness. Integration will be verified end-to-end via the REST endpoints in checkpoint 4.

**Verify:** `dotnet build src/tsx-aggregator/tsx-aggregator.csproj` (confirms proto compiles and handlers build)

---

### 4. REST endpoints

**Build:**

- Add to `CompaniesController`:
  - `POST companies/priority` — accepts `List<string>` body (JSON array of company symbols). Calls gRPC `SetPriorityCompanies`. Returns 200 OK on success, 400/500 on failure.
  - `GET companies/priority` — calls gRPC `GetPriorityCompanies`. Returns 200 with JSON array of pending company symbols.
- Follow the existing `IgnoreRawReport` / `GetStocksWithUpdatedRawDataReports` patterns for error handling and response formatting.

**Test:** No new unit tests — the controller is a thin pass-through to the gRPC client (consistent with how existing endpoints are untested). Build verification confirms correctness.

**Verify:** `dotnet build src/stock-market-webapi/stock-market.webapi.csproj`

---

### 5. Angular System Controls page

**Build:**

- Create `deep-value/src/app/system-controls/` directory with:
  - `system-controls.component.ts` — Component with:
    - `pageTitle = 'System Controls'`
    - `prioritySymbols: string[] = []` (current queue from GET)
    - `newSymbolsInput: string = ''` (text input binding)
    - `loading`, `errorMsg`, `successMsg` state
    - `loadPriorityQueue()` — calls GET, populates `prioritySymbols`
    - `setPriorityCompanies()` — parses comma-separated input, calls POST, refreshes queue
    - `clearPriorityQueue()` — calls POST with empty array, refreshes queue
    - `ngOnInit()` calls `loadPriorityQueue()`
  - `system-controls.component.html` — Layout with:
    - Page title
    - "Priority Companies" section heading
    - `mat-form-field` with text input for comma-separated symbols
    - "Set Priority" button (`mat-raised-button`)
    - "Clear Queue" button (`mat-button`)
    - Current queue display (list of pending symbols, or "Queue empty" message)
    - Loading/error/success indicators
  - `system-controls.component.scss` — Minimal styling consistent with existing pages
- Add service methods to `company.service.ts`:
  - `setPriorityCompanies(symbols: string[]): Observable<any>` — POST to `companies/priority`
  - `getPriorityCompanies(): Observable<string[]>` — GET from `companies/priority`
- Register in `app.module.ts`: import and add to `declarations`
- Add route in `app-routing.module.ts`: `{ path: 'system-controls', component: SystemControlsComponent }` (before the wildcard route)
- Add sidebar nav item in `app.component.html`: `<a mat-list-item routerLink="/system-controls" routerLinkActive="active">System Controls</a>`
- Add i18n keys to `text.service.ts`:
  - `system_controls_title`, `system_controls_priority_heading`, `system_controls_input_label`, `system_controls_set_button`, `system_controls_clear_button`, `system_controls_queue_empty`, `system_controls_success`, `system_controls_error`
- Add translations to `en-US.json` and `zh-CN.json`

**Test:** No unit tests for this checkpoint — Angular component tests would require Karma/Jasmine setup and are consistent with the existing project pattern (no component tests exist in the codebase). Verify by building.

**Verify:** `cd deep-value && npm run build`

---

## Metadata

### Status

success

### Dependencies

- All checkpoints depend on the research findings in `.prompts/override-raw-collector-priority-research/research.md`
- Checkpoint 2 depends on checkpoint 1 (FSM uses Registry priority methods)
- Checkpoint 3 depends on checkpoint 2 (gRPC handlers reference input types)
- Checkpoint 4 depends on checkpoint 3 (REST calls gRPC client)
- Checkpoint 5 depends on checkpoint 4 (Angular calls REST endpoints)

### Open Questions

- None (all resolved during research)

### Assumptions

- Priority override is transient (in-memory only)
- Company symbol alone identifies what to fetch (first matching instrument)
- Round-robin resumes from where it left off after priority queue drains
- PrevInstrumentKey is NOT updated for priority fetches
