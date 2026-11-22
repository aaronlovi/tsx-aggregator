# Stock Data Solution

This solution is composed of several components that work together to fetch, aggregate, and serve stock data.

## Components

### 1. Database

Stores all the data used and produced by the other components.

### 2. Raw Collector

Fetches raw data for instruments. Its operations include:

- Fetching the current instrument directory from the TSX website
- Fetching raw data for a single instrument

### 3. Aggregator

In a loop, it:

- Fetches the next instrument in the database that has fresh unaggregated raw data
- Produces a company report of annual and non-annual raw cash flow, income, and balance reports, as well as processed cash flow reports consisting of several relevant financial measures
- Writes the company report to the database

### 4. Quote Service

At startup, and every 2 hours afterwards, it fetches and caches the list of per-share-prices for every instrument from a Google sheet. It can return the cached per-share-price of a given list of instrument symbols.

### 5. Search Service

Maintains search tries by company name and instrument symbols for use by the web API when an end-user is searching for a company. At startup, and every 5 minutes, it rebuilds the search tries. It can return up to 5 results from the search tries for a given prefix.

### 6. Stock Data Service

Responsible for serving requests from the outside world. Its operations include:

- GetStocksData: For a given exchange (currently only "TSX"), returns all the processed aggregated data for each current instrument, together with per-share prices
- GetStocksDetail: For a given instrument symbol and exchange, gets the processed aggregated data for the specific instrument
- GetStockSearchResults: For a given search term (a string), uses the search service to do the trie searches and returns up to 5 results

### 7. Top-Score Alert Service

Checks every `CheckIntervalMinutes` (default 60) which companies score 13 or more of the 15 checks. Its first check after startup only records the list; after that, when a company joins or leaves the list, it sends an email with the previous list, the new list and the change. The previous list is kept in memory, so a restart starts a new baseline.

**Setup:**

1. The service is on by default (`HostedServices:RunTopScoreAlertService` in `src/tsx-aggregator/appsettings.json`). Without SMTP settings it logs an error at startup and exits; the other services keep running.

2. Set the SMTP credentials and recipients with .NET User Secrets, so no address or password goes into git (from the `src/tsx-aggregator` directory):
   ```bash
   cd src/tsx-aggregator
   dotnet user-secrets set "AlertSettings:SmtpUsername" "your-address@gmail.com"
   dotnet user-secrets set "AlertSettings:SmtpPassword" "your-app-password"
   dotnet user-secrets set "AlertSettings:SenderEmail" "your-address@gmail.com"
   dotnet user-secrets set "AlertSettings:Recipients:0" "where-alerts-go@example.com"
   ```
   Add more recipients as `Recipients:1`, `Recipients:2`, and so on. The startup configuration log masks the password.

   The default SMTP host is `smtp.gmail.com` (port 587, STARTTLS). Gmail needs an **app password**, not your account password: turn on 2-Step Verification, then create one at https://myaccount.google.com/apppasswords. To use another provider, set `AlertSettings:SmtpHost` and `AlertSettings:SmtpPort` (user secrets or `appsettings.json`); it must accept a username and password over SMTP.

   **Outlook.com / Hotmail no longer works:** Microsoft has disabled basic authentication for SMTP, app passwords included (`535 5.7.139 Authentication unsuccessful, basic authentication is disabled`). It would need OAuth, which the service doesn't support.

3. To test the setup, start once with `AlertSettings__SendTestEmailOnStartup=true`: it emails the current list on the first check. To run without alerts, set `HostedServices__RunTopScoreAlertService=false`.

## Web API

The Web API provides the following operations:

- GetStocksData: For a given exchange (currently only "TSX"), returns all the processed aggregated data for each current instrument, together with per-share prices
- GetStocksDetail: For a given instrument symbol and exchange, gets the processed aggregated data for the specific instrument
- GetStockSearchResults: For a given search term (a string), uses the search service to do the trie searches and returns up to 5 results

## License

This source code is made available for reading and reference purposes only. You may not use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of this software without explicit permission from the author.

**All Rights Reserved**

## Contact

Aaron Lovi - aaronlovi@gmail.com
