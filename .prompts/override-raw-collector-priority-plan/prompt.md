# Plan: Override Raw Collector Instrument Priority

## Context
- Research: `.prompts/override-raw-collector-priority-research/research.md`
- Guidelines: `CLAUDE.md`

## Resolved Decisions
- Setting priority companies immediately triggers a fetch (resets the 4-minute timer)
- A GET endpoint to inspect the current priority queue is required
- Clearing the priority queue is done via an empty list POST
- A "System Controls" Angular page is required with a section for priority company management

## Instructions
1. Read research.md
2. Design implementation as checkpoints
3. Each checkpoint must include:
   - Build: what to implement
   - Test: what unit tests to write for THIS checkpoint's code
   - Verify: how to confirm all existing + new tests pass before moving on
4. NEVER design a dedicated "testing" checkpoint at the end. Tests are written alongside the code they verify, within the same checkpoint. Each checkpoint must leave the test suite green.

## Scope

### Backend (tsx-aggregator service)
- Add a priority queue (`Queue<string>`) to `Registry` for company symbols
- Add methods to `Registry`: enqueue priority companies, dequeue next priority, get current queue, clear queue
- Modify `RawCollectorFsm.ProcessUpdateTime()` to check priority queue before round-robin
- Add `RawCollectorSetPriorityCompaniesInput` input message type
- Handle new input in `RawCollector.PreprocessInputs()`
- Add `RawCollectorGetPriorityCompaniesInput` input message type for GET
- Reset `NextFetchInstrumentDataTime` to `DateTime.UtcNow` when priority companies are set (immediate trigger)

### Backend (gRPC + REST)
- Add `SetPriorityCompanies` gRPC message and RPC method in `requests.proto`
- Add `GetPriorityCompanies` gRPC message and RPC method in `requests.proto`
- Implement handlers in `StockDataSvc`
- Add `POST companies/priority` REST endpoint in `CompaniesController`
- Add `GET companies/priority` REST endpoint in `CompaniesController`

### Frontend (Angular)
- Create `SystemControlsComponent` with page for managing priority companies
- Add route `/system-controls`
- Add sidebar navigation item
- Add service methods for POST and GET priority companies
- Add i18n text keys and translations (`en-US.json`, `zh-CN.json`)
- UI: text input for comma-separated company symbols, submit button, display current queue, clear button

### Key patterns to follow
- Input message pattern: extend `RawCollectorInputBase`, use `TaskCompletionSource` for response
- gRPC routing: `StockDataSvc` creates input, posts to `_rawCollector.PostRequest()`, awaits `Completed.Task`
- REST: `CompaniesController` calls gRPC client method
- Angular: `@Injectable` service with `HttpClient`, `Observable<T>` return, `map()` pipe
- Angular components: NgModule-based (not standalone), declared in `AppModule`
- i18n: keys in `TextService`, translations in `en-US.json` and `zh-CN.json`, template `{{ key | translate }}`

## Output
Write plan to `.prompts/override-raw-collector-priority-plan/plan.md`:
- Ordered checkpoints (implementation + tests each — no checkpoint without tests unless it is purely non-code work like documentation or configuration)
- Files to create/modify
- Metadata block (Status, Dependencies, Open Questions, Assumptions)
