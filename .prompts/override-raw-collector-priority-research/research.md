# Research Findings: Override Raw Collector Instrument Priority

## 1. Registry Instrument Selection

**How `GetNextInstrumentKey()` works** (`Registry.cs:99-111`):

- Instruments are stored in a `List<InstrumentDto>` sorted by `InstrumentKey.CompareBySymbols()` (CompanySymbol → InstrumentSymbol → Exchange, lexicographic).
- The method iterates through all instruments, finds the first one where `CompareBySymbols(prevKey, curKey) < 0` (i.e., the next one alphabetically after the previous key).
- If `prevKey` is the last instrument (or past the end), it wraps around to the first instrument.
- The list uses `lock(_instruments)` for thread safety in mutation methods, but `GetNextInstrumentKey()` itself does NOT lock (potential thread-safety issue, but not relevant to priority override design).

**Data structure**: Simple sorted `List<InstrumentDto>`. No priority queue. No pluggable selection strategy.

**Override injection approach**: The cleanest option is to add a priority queue/list to `Registry` (or to `RawCollectorFsm`) that is checked BEFORE falling through to the existing `GetNextInstrumentKey()` round-robin. This keeps the existing behavior as the fallback with zero changes to the round-robin logic.

## 2. Input Channel Pattern

**Existing input message types** (`RawCollectorInputs.cs`):

| Class | Purpose | How dispatched |
|-------|---------|----------------|
| `RawCollectorTimeoutInput` | Regular timer tick | FSM `ProcessUpdateTime()` |
| `RawCollectorPauseServiceInput` | Pause/resume service | FSM `ProcessPauseServiceInput()` |
| `RawCollectorIgnoreRawReportInput` | Ignore a specific raw report | `PreprocessInputs()` (before FSM update) |
| `RawCollectorGetStocksWithUpdatedRawDataReportsRequestInput` | Query updated reports | `PreprocessInputs()` (before FSM update) |
| `RawCollectorGetInstrumentsWithNoRawReportsInput` | Query instruments with no data | `PreprocessInputs()` (before FSM update) |

**Dispatch flow** (`RawCollector.cs:66-77`):

1. Input arrives on `_inputChannel`
2. `PreprocessInputs(input)` — handles query/ignore requests (these are request-response via `TaskCompletionSource`)
3. `_stateFsm.Update(input, utcNow, output)` — FSM processes the input, produces output actions
4. `ProcessOutput(input, output.OutputList)` — executes output actions (fetch directory, fetch instrument data, persist state)

**Adding a new input type**: Straightforward — create `RawCollectorSetPriorityCompaniesInput` extending `RawCollectorInputBase`. It can be handled in `PreprocessInputs()` (to populate the priority list) and optionally wake the FSM (via `ProcessUpdateTime`) to immediately start processing the first priority company.

## 3. FSM State Persistence

**How `ApplicationCommonState` is saved/restored**:

- **Read**: `GetApplicationCommonStateStmt` runs `SELECT next_fetch_directory_time, next_fetch_instrument_data_time, prev_company_symbol, prev_instrument_symbol, next_fetch_stock_quote_time FROM state_fsm_state`
- **Write**: `UpdateStateFsmStateStmt` runs `UPDATE state_fsm_state SET next_fetch_directory_time = @..., next_fetch_instrument_data_time = @..., prev_company_symbol = @..., prev_instrument_symbol = @...`
- The `state_fsm_state` table is a single-row table (no WHERE clause in queries).
- The `IsDirty` flag on `ApplicationCommonState` triggers persistence when state changes.

**Where to persist the priority list**:

| Option | Pros | Cons |
|--------|------|------|
| `appsettings.json` | Simple, no DB changes | Requires restart; not runtime-configurable |
| `state_fsm_state` table (new column) | Persists across restarts; uses existing write pattern | Mixes transient override with persistent FSM state; schema migration needed |
| New DB table | Clean separation; can track history | More DB work; may be overkill for a small list |
| In-memory only (API call) | Simplest; no DB/schema changes; instant | Lost on restart; acceptable for an override that's inherently transient |
| `appsettings.json` + in-memory API | Static defaults + runtime override | Two mechanisms to understand |

**Recommendation**: In-memory only (via API call) is the best fit. A priority override is inherently a transient "fetch these next" command, not persistent state. If the service restarts, the round-robin resumes normally — which is the expected behavior.

## 4. gRPC + REST Exposure

**Existing gRPC service methods** (`requests.proto`):

- `GetStocksData`, `GetStocksDetail`, `GetStockSearchResults` — data queries
- `GetStocksWithUpdatedRawDataReports` — query for updated reports (routes to RawCollector)
- `IgnoreRawDataReport` — mark reports to ignore (routes to RawCollector)
- `GetInstrumentsWithNoRawReports` — query instruments with no data (routes to RawCollector)

**How gRPC → RawCollector routing works** (`StockDataService.cs`):

- `StockDataSvc` holds a reference to `_rawCollector` (injected via DI)
- Creates an input message, posts it via `_rawCollector.PostRequest(input)`
- Awaits `input.Completed.Task` for the response

**Existing REST endpoints** (`CompaniesController.cs`):

- `GET companies/updated_raw_data_reports` → gRPC `GetStocksWithUpdatedRawDataReports`
- `POST companies/ignore_raw_report/{instrumentId}/{instrumentReportIdToKeep}` → gRPC `IgnoreRawDataReport`
- `GET companies/missing_data` → gRPC `GetInstrumentsWithNoRawReports`

**Pattern for a new endpoint**: Follow the same pattern:

1. Add a new gRPC message + service method in `requests.proto`
2. Add handler in `StockDataSvc` that creates a `RawCollectorSetPriorityCompaniesInput`, posts it to `_rawCollector`, awaits completion
3. Add REST endpoint in `CompaniesController` that calls the gRPC method

## 5. Company Symbol Resolution

**How the system resolves symbols**: `InstrumentKey` requires the full tuple `(CompanySymbol, InstrumentSymbol, Exchange)`. However:

- `Registry.GetInstrument(InstrumentKey k)` does a binary search comparing by all three fields.
- In the TSX data, `CompanySymbol` and `InstrumentSymbol` are often identical (e.g., "CNQ" / "CNQ"), but not always — a company can have multiple instruments (common shares, preferred shares, warrants, debentures).
- The `_instruments` list is sorted by CompanySymbol first, so all instruments for a company are adjacent.

**For the priority override**: Accepting just the `CompanySymbol` (e.g., "CNQ") is the most user-friendly approach. Resolution options:

1. **Pick the first instrument for that company** — iterate through `_instruments`, find the first with matching `CompanySymbol`. Simple and usually correct (common shares are typically the first/only non-preferred instrument).
2. **Process all instruments for that company** — find all instruments with that CompanySymbol, process them all. More thorough but potentially slow if the company has many instrument classes.
3. **Accept full InstrumentKey** — most precise but least user-friendly.

**Recommendation**: Accept `CompanySymbol` only. Resolve to the first matching instrument (option 1). If the user wants a specific instrument, they can provide the full symbol.

## 6. Configuration Approach (Recommendation)

**Recommended: In-memory API call (transient)**

The priority override is best modeled as a transient queue of company symbols:

- User POSTs a list of company symbols (e.g., `["CNQ", "SU", "TD"]`)
- The RawCollector drains the priority queue before resuming round-robin
- Once drained, normal alphabetical cycling resumes
- If the service restarts, the priority list is lost (acceptable — it's a "do this now" command)

This avoids DB schema changes, config file changes, and complex persistence logic. It matches the mental model of "override the next few companies to process."

**Alternative considered**: `appsettings.json` with `PriorityCompanies` array. This would work for a static list that always gets processed first on startup, but it requires a restart to change and conflates "always prioritize" with "prioritize once." If the user wants the list to be permanent, this could be added later as a secondary mechanism.

## 7. Edge Cases

| Edge case | Recommended handling |
|-----------|---------------------|
| Priority company not in `instruments` table | Skip it, log a warning, continue to next priority item |
| Priority company is obsoleted | Skip it (obsoleted instruments aren't in the `_instruments` list — they're filtered out at load time) |
| Priority list exhausted | Fall back to normal round-robin from `PrevInstrumentKey` |
| Duplicate company in priority list | Process it once, skip duplicates (use a `HashSet` or dedup on insertion) |
| Priority list set while already processing a priority item | Replace/append to existing priority queue |
| Service paused | Priority list should be ignored while paused (existing pause check in `ProcessUpdateTime` handles this) |
| Company symbol has multiple instruments | Process the first matching instrument (common shares) |

## Existing Patterns to Follow

1. **Input message pattern**: Create a new `RawCollectorInputBase` subclass with the priority list data and a `TaskCompletionSource` for the response.
2. **PreprocessInputs dispatch**: Add a new `is` check in `RawCollector.PreprocessInputs()` to handle the new input type.
3. **gRPC service method**: Add to `StockDataService.StockDataServiceBase` in `requests.proto`, implement in `StockDataSvc`.
4. **REST endpoint**: Add to `CompaniesController` following the existing `POST` pattern.
5. **Registry modification**: Add a method like `GetNextPriorityInstrumentKey()` that checks the priority queue first, or modify `GetNextInstrumentKey()` to accept an optional priority override.

## Risks and Concerns

1. **Thread safety**: The priority queue in `Registry` needs to be thread-safe (use `lock` like the existing `_instruments` list, or use a `ConcurrentQueue`).
2. **Priority queue vs. FSM timing**: The priority queue should bypass the 4-minute wait. When a priority is set, the FSM should immediately schedule the next fetch (set `NextFetchInstrumentDataTime` to now).
3. **Scope creep**: Keep this feature simple — a transient queue of company symbols, not a full priority/weight system.
4. **No UI planned**: This is a backend-only override for now. A future Angular UI could call the REST endpoint.

## Recommended Approach

### Minimal viable implementation

1. **Add `Queue<string>` to `Registry`** (or a new field on `RawCollectorFsm`) for priority company symbols.
2. **Modify `RawCollectorFsm.ProcessUpdateTime()`**: Before calling `_registry.GetNextInstrumentKey()`, check if the priority queue has items. If so, dequeue the next symbol, resolve it to an `InstrumentKey`, and use that instead.
3. **Add `RawCollectorSetPriorityCompaniesInput`**: New input message carrying `IReadOnlyList<string> CompanySymbols`.
4. **Handle in `PreprocessInputs()`**: Populate the priority queue in `Registry` (or `RawCollectorFsm`).
5. **Add gRPC method**: `SetPriorityCompanies(SetPriorityCompaniesRequest) returns (StockDataServiceReply)`.
6. **Add REST endpoint**: `POST companies/priority` with a JSON body of `["CNQ", "SU", "TD"]`.
7. **Optionally reset the fetch timer**: When priority companies are set, set `NextFetchInstrumentDataTime = DateTime.UtcNow` so the next priority company is fetched immediately instead of waiting up to 4 minutes.

## Metadata

### Status

success

### Dependencies

- `Registry.cs` — will need modification to support priority queue
- `RawCollectorFsm.cs` — will need modification to check priority queue before round-robin
- `requests.proto` — will need a new message and RPC method
- `StockDataService.cs` and `CompaniesController.cs` — will need new endpoint plumbing

### Open Questions (Resolved)

- Should setting priority companies immediately trigger a fetch (reset the 4-minute timer), or wait for the next natural cycle? **Decision: Trigger immediately**
- Should there be a way to GET the current priority queue (inspect what's pending)? **Decision: Yes**
- Should there be a way to CLEAR the priority queue without setting a new one? **Decision: Yes (empty list POST)**

### Additional Requirements (Post-Research)

- Add a "System Controls" page to the Angular frontend with a section for priority company management (set, view, clear)

### Assumptions

- The priority override is transient (in-memory only, not persisted to DB)
- Company symbol alone is sufficient to identify what to fetch (resolves to first matching instrument)
- The existing round-robin resumes after the priority queue is drained
- A frontend UI IS needed — "System Controls" page in Angular
