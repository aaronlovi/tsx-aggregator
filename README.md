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

### 7. Score-13 Alert Service

Periodically checks whether the list of companies passing all 13 scoring checks has changed. When a change is detected, sends an email alert containing the previous list, the new list, and the diff (added/removed tickers).

**Setup:**

1. Enable the service in `src/tsx-aggregator/appsettings.json`:
   ```json
   "HostedServices": {
       "RunScore13AlertService": true
   }
   ```

2. Configure recipients in `src/tsx-aggregator/appsettings.json`:
   ```json
   "AlertSettings": {
       "Recipients": ["your-email@example.com"]
   }
   ```

3. Set SMTP credentials using .NET User Secrets (from the `src/tsx-aggregator` directory):
   ```bash
   cd src/tsx-aggregator
   dotnet user-secrets set "AlertSettings:SmtpUsername" "your-email@hotmail.com"
   dotnet user-secrets set "AlertSettings:SmtpPassword" "your-app-password"
   dotnet user-secrets set "AlertSettings:SenderEmail" "your-email@hotmail.com"
   ```

   The default SMTP host is `smtp-mail.outlook.com` (port 587, STARTTLS). To use a different provider, update `SmtpHost` and `SmtpPort` in `appsettings.json`.

   **Note:** If your Microsoft account has two-factor authentication enabled, you must create an app password at https://account.microsoft.com/security instead of using your regular password.

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
