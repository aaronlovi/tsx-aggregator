# Research: Override Raw Collector Instrument Priority

## Objective
Understand how the RawCollector selects the next instrument to process, and determine the best approach to allow users to override that selection with a priority list of companies (e.g., "fetch CNQ next").

## Context
- Guidelines: `CLAUDE.md`
- The RawCollector is an FSM-based BackgroundService in `src/tsx-aggregator/Raw/`
- It cycles through instruments alphabetically every 4 minutes via `Registry.GetNextInstrumentKey()`
- The FSM state (including `PrevInstrumentKey`) is persisted in the `state_fsm_state` DB table
- There is an existing channel-based input mechanism (`_inputChannel`) that accepts various `RawCollectorInputBase` messages
- There is no current mechanism to override instrument processing order

## Questions to Answer

1. **Registry instrument selection**: How exactly does `Registry.GetNextInstrumentKey()` work? What data structure holds the instruments, and could a priority queue or override list be injected without breaking the existing round-robin cycle?

2. **Input channel pattern**: What input message types already exist for `RawCollectorInputBase`? How are they dispatched in the main loop? Could a new `RawCollectorPriorityOverrideInput` message type be added following the same pattern?

3. **FSM state persistence**: How is `ApplicationCommonState` saved and restored? If we add a priority list, where should it be persisted — in the same `state_fsm_state` table, a new table, or in-memory only (config/appsettings)?

4. **gRPC + REST exposure**: What existing gRPC service methods and REST endpoints exist for controlling the RawCollector (e.g., pause/resume, ignore report)? Could a new "set priority companies" endpoint follow the same patterns?

5. **Company symbol resolution**: How does the system resolve a company symbol like "CNQ" to an `InstrumentKey`? Is there a lookup from company symbol alone, or do we always need the full (CompanySymbol, InstrumentSymbol, Exchange) tuple? What happens if a company has multiple instruments?

6. **Configuration approach**: Should the priority list be configurable via `appsettings.json` (static, requires restart), a database table (persistent, survives restart), an in-memory API call (transient), or some combination? What are the tradeoffs?

7. **Edge cases**: What happens if a priority company isn't in the `instruments` table? What if it's been obsoleted? What if the priority list is exhausted — should it fall back to normal round-robin?

## Explore

- `src/tsx-aggregator/Raw/RawCollector.cs` — Main loop, input channel handling, message dispatch
- `src/tsx-aggregator/Raw/RawCollectorFsm.cs` — FSM states, `ProcessUpdateTime`, timeout logic
- `src/tsx-aggregator/Raw/Registry.cs` — `GetNextInstrumentKey()`, instrument storage
- `src/tsx-aggregator/Raw/RawCollectorInputs.cs` or similar — Input message types
- `src/tsx-aggregator.models/DataTransferObjects.cs` — `ApplicationCommonState`, `InstrumentKey`
- `src/dbm-persistence/Statements/` — FSM state read/write queries
- `src/tsx-aggregator.shared/Protos/` — gRPC proto definitions for existing control endpoints
- `src/stock-market-webapi/Controllers/` — REST endpoints that forward to gRPC
- `appsettings.json` — Existing configuration patterns for hosted services and feature flags

## Output
Write findings to `.prompts/override-raw-collector-priority-research/research.md`:
- Answers to the questions above
- Existing patterns to follow
- Risks or concerns
- Recommended approach (with alternatives considered)
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
