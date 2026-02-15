# Plan: Score-13 Email Alert System

## Overview

Add a `Score13AlertService` BackgroundService to tsx-aggregator that periodically computes the score-13 company list, detects changes, and sends an email via MailKit containing the previous list, new list, and diff (added/removed tickers).

## Checkpoint 1: Configuration & Options Classes

### Build

Add the `AlertSettings` options class and wire up configuration.

**Create** `src/tsx-aggregator.models/AlertSettingsOptions.cs`:
- New options class `AlertSettingsOptions` with:
  - `const string AlertSettings = "AlertSettings"` (section name)
  - `SmtpHost` (string, required) — e.g., `smtp.gmail.com`
  - `SmtpPort` (int, required) — e.g., `587`
  - `SmtpUsername` (string, required) — sender email address
  - `SmtpPassword` (string, required) — from User Secrets or environment variable
  - `SenderEmail` (string, required) — from address
  - `Recipients` (string[], required) — list of recipient email addresses
  - `CheckIntervalMinutes` (int, required) — how often to check (e.g., `60`)
- All properties `[Required]` with error messages, following the `HostedServicesOptions` pattern
- **Important**: `AddValidatedOptions` runs at startup regardless of whether the service is enabled. Since SMTP settings will be empty when `RunScore13AlertService: false`, do NOT use `AddValidatedOptions`. Instead, use `.Configure<>()` only and validate manually inside `Score13AlertService.ExecuteAsync()` before entering the loop (log error and return early if config is invalid).

**Modify** `src/tsx-aggregator.models/HostedServicesOptions.cs`:
- Add `RunScore13AlertService` (bool?, `[Required]`)

**Modify** `src/tsx-aggregator/appsettings.json`:
- Add `"RunScore13AlertService": false` to `HostedServices` section
- Add new `AlertSettings` section with placeholder values (empty strings for secrets):
  ```json
  "AlertSettings": {
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": 587,
    "SmtpUsername": "",
    "SmtpPassword": "",
    "SenderEmail": "",
    "Recipients": [],
    "CheckIntervalMinutes": 60
  }
  ```

**Modify** `src/tsx-aggregator/Program.cs`:
- Add `.Configure<AlertSettingsOptions>(context.Configuration.GetSection(AlertSettingsOptions.AlertSettings))` alongside existing `.Configure<>` calls (line 56)
- Do NOT add `AddValidatedOptions` for `AlertSettingsOptions` — validation is done at runtime in the service (see note above)

**Modify** `src/Directory.Packages.props`:
- Add `<PackageVersion Include="MailKit" Version="4.12.0" />` (check latest stable)

**Modify** `src/tsx-aggregator/tsx-aggregator.csproj`:
- Add `<PackageReference Include="MailKit" />`

### Test

**Create** `src/tsx-aggregator.tests/AlertSettingsOptionsTests.cs`:
- Test that `AlertSettingsOptions` properties are correctly initialized
- Test that `[Required]` validation fails when required fields are missing (use `Validator.TryValidateObject`)
- Test that `HostedServicesOptions.RunScore13AlertService` exists and is nullable bool

### Verify
```bash
cd src && dotnet build tsx-aggregator.sln && dotnet test tsx-aggregator.tests/
```

---

## Checkpoint 2: Score-13 Diff Computation Logic

### Build

Create pure logic for computing the score-13 list and diff, independent of email or BackgroundService concerns.

**Create** `src/tsx-aggregator.models/Score13AlertData.cs`:
- `Score13Company` record: `(string InstrumentSymbol, string CompanyName)` — implements `IComparable<Score13Company>` for sorted output
- `Score13Diff` record: `(IReadOnlyList<Score13Company> PreviousList, IReadOnlyList<Score13Company> NewList, IReadOnlyList<Score13Company> Added, IReadOnlyList<Score13Company> Removed)`
- `Score13DiffComputer` static class with:
  - `ComputeScore13List(IReadOnlyList<CompanyFullDetailReport> reports)` → `IReadOnlyList<Score13Company>`: filters by `OverallScore == 13`, returns sorted list
  - `ComputeDiff(IReadOnlyList<Score13Company> previous, IReadOnlyList<Score13Company> current)` → `Score13Diff?`: returns null if no change, otherwise the diff
  - `FormatAlertBody(Score13Diff diff)` → `string`: produces the plain-text email body per the format in the prompt
  - `FormatAlertSubject(Score13Diff diff)` → `string`: produces subject line like `"TSX Score-13 Alert: 2 added, 1 removed"`

### Test

**Create** `src/tsx-aggregator.tests/Score13DiffComputerTests.cs`:
- Test `ComputeScore13List` with mix of scores (some 13, some not) — verify only score-13 companies returned, sorted by symbol
- Test `ComputeScore13List` with empty input → empty list
- Test `ComputeScore13List` with companies missing prices (PricePerShare=0, which means OverallScore can't be 13) → excluded
- Test `ComputeDiff` with identical lists → returns null
- Test `ComputeDiff` with additions only → correct Added, empty Removed
- Test `ComputeDiff` with removals only → empty Added, correct Removed
- Test `ComputeDiff` with both additions and removals → correct diff
- Test `ComputeDiff` from empty to non-empty (first meaningful change) → all items in Added
- Test `FormatAlertSubject` with known diff → correct subject
- Test `FormatAlertBody` with known diff → body contains all sections (New List, Previous List, Added, Removed) with correct tickers

Use `TestDataFactory` pattern from existing tests if helpful, or create `CompanyFullDetailReport` instances directly (the constructor takes 16 params — use helper methods for test readability).

### Verify
```bash
cd src && dotnet build tsx-aggregator.sln && dotnet test tsx-aggregator.tests/
```

---

## Checkpoint 3: Email Sending Service

### Build

Create the email sending abstraction using MailKit.

**Create** `src/tsx-aggregator/Services/IEmailService.cs`:
- Interface `IEmailService` with method `Task<bool> SendEmailAsync(string subject, string body, CancellationToken ct)`
- Returns `true` on success, `false` on failure (service logs the error)

**Create** `src/tsx-aggregator/Services/EmailService.cs`:
- Class `EmailService : IEmailService`
- Constructor injects `IOptions<AlertSettingsOptions>` and `ILogger<EmailService>`
- `SendEmailAsync` implementation:
  - Creates `MimeMessage` with `From` = `SenderEmail`, `To` = each recipient from `AlertSettings.Recipients`
  - Plain text body (no HTML)
  - Connects to SMTP via MailKit's `SmtpClient` (not `System.Net.Mail.SmtpClient`)
  - Uses `SecureSocketOptions.StartTls` for port 587
  - Authenticates with `SmtpUsername` and `SmtpPassword`
  - Wraps in try/catch, logs errors, returns false on failure

**Modify** `src/tsx-aggregator/Program.cs`:
- Register `IEmailService` as singleton: `.AddSingleton<IEmailService, EmailService>()`

### Test

**Create** `src/tsx-aggregator.tests/EmailServiceTests.cs`:
- Test that `EmailService` constructor handles valid options without throwing
- Test that `SendEmailAsync` with unreachable SMTP host returns false (does not throw)
- Use Moq to mock `IOptions<AlertSettingsOptions>` and `ILogger<EmailService>`

Note: Full SMTP integration testing requires a real server — unit tests focus on error handling and non-throwing behavior.

### Verify
```bash
cd src && dotnet build tsx-aggregator.sln && dotnet test tsx-aggregator.tests/
```

---

## Checkpoint 4: Score13AlertService BackgroundService

### Build

Create the main BackgroundService that ties together score computation, diff detection, and email sending.

**Create** `src/tsx-aggregator/Services/Score13AlertService.cs`:
- Class `Score13AlertService : BackgroundService`
- Constructor injects:
  - `IStocksDataRequestsProcessor` — to get stock data from DB
  - `IQuoteService` — to fill prices and gate on `QuoteServiceReady`
  - `IEmailService` — to send alert emails
  - `IOptions<AlertSettingsOptions>` — for check interval
  - `ILogger<Score13AlertService>`
- Private field `IReadOnlyList<Score13Company>? _previousList` (null = first run, no alert)
- `ExecuteAsync` logic:
  1. Validate `AlertSettingsOptions` — check that `SmtpHost`, `SmtpUsername`, `SmtpPassword`, `SenderEmail`, `Recipients` are non-empty. If invalid, log error and return (exit service gracefully).
  2. Await `_quoteService.QuoteServiceReady.Task` with cancellation (same pattern as `StockDataSvc.GetStocksData()` line 48)
  3. Loop with `Task.Delay(CheckIntervalMinutes)`:
     a. Post `GetStocksForExchangeRequest` (exchange="TSX") to `IStocksDataRequestsProcessor` and await result (same pattern as `StockDataSvc`, lines 51-61)
     b. Post `QuoteServiceFillPricesForSymbolsInput` to `IQuoteService` and await result (same pattern as `StockDataSvc`, lines 69-80)
     c. Remove items missing prices (same as `StockDataSvc`, lines 82-91)
     d. Construct `CompanyFullDetailReport` for each item (same as `CompaniesController.GetDashboardAggregates()`, lines 336-354)
     e. Call `Score13DiffComputer.ComputeScore13List(reports)` to get current list
     f. If `_previousList` is null (first run): set `_previousList = currentList`, log, continue
     g. Call `Score13DiffComputer.ComputeDiff(_previousList, currentList)` — if null (no change), continue
     h. Format subject and body via `Score13DiffComputer`
     i. Call `_emailService.SendEmailAsync(subject, body, ct)`
     j. Update `_previousList = currentList` (regardless of email success, to avoid repeat alerts)
  4. Wrap loop body in try/catch, log errors, continue loop (don't crash the service)

**Modify** `src/tsx-aggregator/Program.cs`:
- Register `Score13AlertService` as singleton (like `Aggregator`, `RawCollector`)
- Add toggle: `if (hostedServicesOptions.RunScore13AlertService ?? false) services.AddHostedService(p => p.GetRequiredService<Score13AlertService>())`

### Test

**Create** `src/tsx-aggregator.tests/Score13AlertServiceTests.cs`:
- Test that the service can be constructed with mocked dependencies
- Test the data flow: mock `IStocksDataRequestsProcessor` and `IQuoteService` to return known data, verify `IEmailService.SendEmailAsync` is called with expected subject/body when score-13 list changes
- Test that no email is sent on first run (baseline establishment)
- Test that no email is sent when the list hasn't changed
- Test that the service handles exceptions from `IStocksDataRequestsProcessor` gracefully (logs, doesn't crash)
- Test that the service exits gracefully when `AlertSettingsOptions` has empty/missing SMTP config

For mocking `IStocksDataRequestsProcessor.PostRequest()` and `IQuoteService.PostRequest()`: these return `bool` and communicate results via `TaskCompletionSource<object?>` on the input object. The test will need to:
1. Capture the input object when `PostRequest` is called
2. Set the `Completed` TaskCompletionSource result with mock data
3. This simulates the request/response pattern used by the channel-based processors

### Verify
```bash
cd src && dotnet build tsx-aggregator.sln && dotnet test tsx-aggregator.tests/
```

---

## Files Summary

| Checkpoint | Files Created | Files Modified |
|------------|---------------|----------------|
| 1 | `tsx-aggregator.models/AlertSettingsOptions.cs`, `tsx-aggregator.tests/AlertSettingsOptionsTests.cs` | `HostedServicesOptions.cs`, `appsettings.json`, `Program.cs`, `Directory.Packages.props`, `tsx-aggregator.csproj` |
| 2 | `tsx-aggregator.models/Score13AlertData.cs`, `tsx-aggregator.tests/Score13DiffComputerTests.cs` | — |
| 3 | `tsx-aggregator/Services/IEmailService.cs`, `tsx-aggregator/Services/EmailService.cs`, `tsx-aggregator.tests/EmailServiceTests.cs` | `Program.cs` |
| 4 | `tsx-aggregator/Services/Score13AlertService.cs`, `tsx-aggregator.tests/Score13AlertServiceTests.cs` | `Program.cs` |

## Metadata
### Status
success
### Dependencies
- `src/tsx-aggregator.models/AggregatorData/CompanyFullDetailReport.cs` — score computation via `OverallScore` property
- `src/tsx-aggregator/Services/StockDataService.cs` — data retrieval pattern to replicate (lines 33-94)
- `src/tsx-aggregator/QuotesService/QuoteService.cs` — `QuoteServiceReady` gate, `PostRequest` pattern
- `src/tsx-aggregator/Services/StocksDataRequestsProcessor.cs` — `PostRequest` pattern for channel-based requests
- `src/tsx-aggregator/Program.cs` — service registration and HostedService toggle pattern
- `.prompts/metrics-monitoring-alerts-research/research.md` — Q4 (email design), Q2 (data flow)
### Open Questions
- MailKit exact latest stable version — verify at implementation time via NuGet
- Whether `IStocksDataRequestsProcessor` and `IQuoteService` need to be running for `Score13AlertService` to work (they should be, since the service depends on them) — the toggle in `appsettings.json` must have both enabled alongside `RunScore13AlertService`
### Assumptions
- Gmail SMTP with app password is the SMTP provider — config is flexible enough for any SMTP server
- In-memory previous list is acceptable (no persistence across restarts — first run establishes baseline silently)
- The test project already references the tsx-aggregator project (verified in `tsx-aggregator.tests.csproj` line 27)
- `CompanyFullDetailReport` constructor and `OverallScore` property are stable and won't change during implementation
