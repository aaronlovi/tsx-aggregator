using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace tsx_aggregator;

internal partial class RawCollector : BackgroundService {

    /// <summary>
    /// This is one of the main functions of the Raw data collector.
    /// Fetches the current instrument directory from the TSX website.
    /// Creates new instruments and obsoletes instruments that are no longer listed.
    /// </summary>
    private async Task ProcessFetchDirectory(CancellationToken ct) {
        _logger.LogInformation("ProcessFetchDirectory begin");

        // Map from company symbol --> instrument symbol --> InstrumentDto
        Dictionary<string, InstrumentDtoByInstrumentNameMap> directory = await FetchInstrumentDirectory(ct);

        UpdateRegistryResults instrumentUpdateResults = await UpdateRegistry(directory, ct);
        var newInstruments = instrumentUpdateResults.NewInstruments;
        var obsoletedInstruments = instrumentUpdateResults.ObsoletedInstruments;

        _logger.LogInformation("ProcessFetchDirectory - new:{NumNewInstruments},obsolete:{NumObsoletedInstruments}",
            newInstruments.Count, obsoletedInstruments.Count);

        if (instrumentUpdateResults.IsEmpty) {
            _logger.LogInformation("ProcessFetchDirectory - no new or obsoleted instruments");
            return;
        }

        IReadOnlyList<InstrumentDto> newInstruments_ = (IReadOnlyList<InstrumentDto>)newInstruments;
        IReadOnlyList<InstrumentDto> obsoletedInstruments_ = (IReadOnlyList<InstrumentDto>)obsoletedInstruments;
        Result res = await _dbm.UpdateInstrumentList(newInstruments_, obsoletedInstruments_, ct);

        if (!res.Success) {
            _logger.LogInformation("ProcessFetchDirectory - end. Success: {Success}. Error: {Error}", res.Success, res.ErrMsg);
            RestoreDbAndRegistryConsistency(instrumentUpdateResults);
            return;
        }

        // Signal that the directory is available for downstream services (e.g. QuoteService)
        _registry.DirectoryInitialized.TrySetResult();

        _logger.LogInformation("ProcessFetchDirectory - end. Success: {Success}", res.Success);
    }

    private async Task<UpdateRegistryResults> UpdateRegistry(
        Dictionary<string, InstrumentDtoByInstrumentNameMap> directory,
        CancellationToken ct) {
        IList<InstrumentDto> newInstrumentList = _registry.GetNewInstruments(directory);
        for (var i = 0; i < newInstrumentList.Count; i++) {
            newInstrumentList[i] = newInstrumentList[i] with { InstrumentId = (long)await _dbm.GetNextId64(ct) };
            _registry.AddInstrument(newInstrumentList[i]);
        }

        IList<InstrumentDto> obsoletedInstrumentList = _registry.GetObsoletedInstruments(directory);
        foreach (var obsoletedInstrument in obsoletedInstrumentList)
            _registry.RemoveInstrument(obsoletedInstrument);

        return new UpdateRegistryResults(newInstrumentList, obsoletedInstrumentList);
    }

    /// <summary>
    /// Fixes the registry when it has gone out of sync with the database
    /// Used if registry update succeeded, but database update failed
    /// </summary>
    private void RestoreDbAndRegistryConsistency(UpdateRegistryResults instrumentUpdateResults) {
        // Database and registry are not consistent with each other now

        // Add back in the obsoleted elements
        foreach (var obsoletedInstrument in instrumentUpdateResults.ObsoletedInstruments)
            _registry.AddInstrument(obsoletedInstrument);

        // Remove the newly inserted elements
        foreach (var newInstrument in instrumentUpdateResults.NewInstruments)
            _registry.RemoveInstrument(newInstrument);

        // Database and registry are back in consistent state
    }

    private async Task<Dictionary<string, InstrumentDtoByInstrumentNameMap>> FetchInstrumentDirectory(CancellationToken ct) {
        var directory = new Dictionary<string, InstrumentDtoByInstrumentNameMap>();
        string[] letters = {
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "0-9"
        };

        try {
            foreach (string curLetter in letters) {
                using HttpRequestMessage request = CreateFetchDirectoryHttpRequest(curLetter);

                _logger.LogInformation("FetchInstrumentDirectory - fetching letter:{Letter}", curLetter);

                using HttpResponseMessage response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) {
                    _logger.LogWarning("FetchInstrumentDirectory - letter:{Letter}, HTTP request failed with status code: {StatusCode}",
                        curLetter, response.StatusCode);
                    continue;
                }

                string json = await response.Content.ReadAsStringAsync(ct);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                JsonElement curLetterDirectory = GetDirectoryForLetter(root, curLetter);
                if (curLetterDirectory.ValueKind == JsonValueKind.Undefined)
                    continue;

                foreach (JsonElement company in curLetterDirectory.EnumerateArray()) {
                    var (companySymbol, companyName, instruments) = ExtractCompanyDetails(company, curLetter);
                    if (companySymbol is null || companyName is null || instruments.ValueKind is JsonValueKind.Undefined)
                        continue;

                    foreach (JsonElement instrument in instruments.EnumerateArray()) {
                        var (instrumentSymbol, instrumentName) = ExtractInstrumentDetails(instrument, curLetter);
                        if (instrumentSymbol is null || instrumentName is null)
                            continue;

                        var dto = new InstrumentDto(
                            0,
                            Constants.TsxExchange,
                            companySymbol,
                            companyName,
                            instrumentSymbol,
                            instrumentName,
                            DateTimeOffset.UtcNow,
                            null);

                        if (!ShouldAddInstrument(dto))
                            continue;

                        _ = directory.TryGetValue(dto.CompanySymbol, out InstrumentDtoByInstrumentNameMap? instrumentMap);
                        if (instrumentMap is null) {
                            _logger.LogInformation("Adding new company symbol to directory: {CompanySymbol}", dto.CompanySymbol);
                            directory[dto.CompanySymbol] = instrumentMap = new InstrumentDtoByInstrumentNameMap();
                        }

                        _ = instrumentMap.TryGetValue(dto.InstrumentSymbol, out InstrumentDto? instrumentFromMap);
                        if (instrumentFromMap is null) {
                            _logger.LogInformation("Adding new instrument symbol to directory: {CompanySymbol},{InstrumentSymbol}",
                                dto.CompanySymbol, dto.InstrumentSymbol);
                            instrumentMap[dto.InstrumentSymbol] = instrumentFromMap = dto;
                        }
                        else {
                            _logger.LogInformation("Instrument already exists in directory, not adding: {Instrument}", dto);
                        }
                    }
                }
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "FetchInstrumentDirectory error");
            directory.Clear();
        }

        return directory;
    }

    private static HttpClient CreateFetchDirectoryHttpClient(IHttpClientFactory _) {
        var handler = new SocketsHttpHandler {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectCallback = async (context, cancellationToken) => {
                // Force IPv4 — TSX's IPv6 endpoints are unreachable
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
        };
        var httpClient = new HttpClient(handler);
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");
        httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        httpClient.DefaultRequestHeaders.Add("cookie", "tmx_locale=en;");
        return httpClient;
    }

    private static HttpRequestMessage CreateFetchDirectoryHttpRequest(string curLetter) {
        var request = new HttpRequestMessage {
            RequestUri = new Uri($"https://www.tsx.com/json/company-directory/search/tsx/{curLetter}"),
            Method = HttpMethod.Get,
            Headers = {
                        { "Referer", "https://www.tsx.com/listings/listing-with-us/listed-company-directory?lang=en" }
                    },
            Content = null
        };
        return request;
    }

    private JsonElement GetDirectoryForLetter(JsonElement root, string curLetter) {
        if (!root.TryGetProperty("results", out JsonElement curLetterDirectory)) {
            _logger.LogWarning("FetchInstrumentDirectory - letter:{Letter} - 'results' property not found", curLetter);
            return default;
        }
        if (curLetterDirectory.ValueKind != JsonValueKind.Array) {
            _logger.LogWarning("FetchInstrumentDirectory - letter:{Letter} - directory is not in array form", curLetter);
            return default;
        }

        return curLetterDirectory;
    }

    private (string?, string?, JsonElement) ExtractCompanyDetails(JsonElement company, string curLetter) {
        if (!company.TryGetProperty("symbol", out JsonElement companySymbol)) {
            _logger.LogWarning("FetchInstrumentDirectory - letter:{Letter},company:{@company} - no 'symbol' property", curLetter, company);
            return (null, null, default);
        }
        if (!company.TryGetProperty("name", out JsonElement companyName)) {
            _logger.LogWarning("FetchInstrumentDirectory - letter:{Letter},company:{@company} - no 'name' property", curLetter, company);
            return (null, null, default);
        }
        if (!company.TryGetProperty("instruments", out JsonElement instruments)
            || instruments.ValueKind != JsonValueKind.Array) {
            _logger.LogWarning("FetchInstrumentDirectory - letter:{Letter},company:{@company} - no 'instruments' property, or not an array", curLetter, company);
            return (null, null, default);
        }

        return (companySymbol.GetString(), companyName.GetString(), instruments);
    }

    private (string?, string?) ExtractInstrumentDetails(JsonElement instrument, string curLetter) {
        if (!instrument.TryGetProperty("symbol", out JsonElement instrumentSymbol)) {
            _logger.LogWarning("FetchInstrumentDirectory - letter:{Letter},instrument:{@instrument} - no 'symbol' property", curLetter, instrument);
            return (null, null);
        }
        if (!instrument.TryGetProperty("name", out JsonElement instrumentName)) {
            _logger.LogWarning("FetchInstrumentDirectory - letter:{Letter},instrument:{@instrument} - no 'name' property", curLetter, instrument);
            return (null, null);
        }

        return (instrumentSymbol.GetString(), instrumentName.GetString());
    }

    private bool ShouldAddInstrument(InstrumentDto dto) {
        if (dto.IsTsxPreferredShares) {
            _logger.LogInformation("Instrument preferred shared, not adding: {Instrument}", dto);
            return false;
        }
        if (dto.IsTsxWarrant) {
            _logger.LogInformation("Instrument is a warrant, not adding: {Instrument}", dto);
            return false;
        }
        if (dto.IsTsxCompanyBonds) {
            _logger.LogInformation("Instrument is company bonds, not adding: {Instrument}", dto);
            return false;
        }
        if (dto.IsTsxETF) {
            _logger.LogInformation("Instrument is an ETF, not adding: {Instrument}", dto);
            return false;
        }
        if (dto.IsTsxBmoMutualFund) {
            _logger.LogInformation("Instrument is a BMO Mutual fund, not adding: {Instrument}", dto);
            return false;
        }
        if (dto.IsPimcoMutualFund) {
            _logger.LogInformation("Instrument is a PIMCO Mutual fund, not adding: {Instrument}", dto);
            return false;
        }
        if (dto.IsTsxMutualFund) {
            _logger.LogInformation("Instrument is a Mutual fund, not adding: {Instrument}", dto);
            return false;
        }
        if (dto.IsTsxCdr) {
            _logger.LogInformation("Instrument is a CDR (CAD Hedged), not adding: {Instrument}", dto);
            return false;
        }
        if (dto.IsTsxPrivatePool) {
            _logger.LogInformation("Instrument is a Private Pool, not adding: {Instrument}", dto);
            return false;
        }
        if (dto.IsTsxFidelityPortfolio) {
            _logger.LogInformation("Instrument is a Fidelity Portfolio, not adding: {Instrument}", dto);
            return false;
        }

        return true;
    }
}
