using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;
using tsx_aggregator.Raw;
using tsx_aggregator.shared;

namespace tsx_aggregator;

internal class RawCollector : BackgroundService {
    private readonly ILogger _logger;
    private readonly IDbmService _dbm;
    private readonly IServiceProvider _svp;
    private readonly Registry _registry;
    private readonly StateFsm _stateFsm;
    private readonly IHttpClientFactory _httpClientFactory;

    public RawCollector(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<Aggregator>>();
        _dbm = svp.GetRequiredService<IDbmService>();
        _svp = svp;
        _registry = svp.GetRequiredService<Registry>();
        _stateFsm = new(DateTime.UtcNow, _registry);
        _httpClientFactory = svp.GetRequiredService<IHttpClientFactory>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {

            await RestoreStateFsm(stoppingToken);
            await RestoreInstrumentDirectory(stoppingToken);

            while (!stoppingToken.IsCancellationRequested) {
                _logger.LogInformation("Raw Worker running at: {time}", DateTimeOffset.Now);

                var utcNow = DateTime.UtcNow;
                var nextTimeout = _stateFsm.NextTimeout;
                var output = new StateFsmOutputs();

                TimeSpan interval = CalcIntervalMs(utcNow, nextTimeout);
                DateTime wakeupTime = utcNow.Add(interval);
                _logger.LogInformation("Sleeping until {WakeupTime}", wakeupTime);

                await Task.Delay(interval, stoppingToken);

                utcNow = DateTime.UtcNow;
                _stateFsm.Update(utcNow, output);
                await ProcessOutput(output.OutputList, stoppingToken);
            }
        }
        catch (OperationCanceledException) {
            _logger.LogInformation("RawCollector - Cancellation encountered, main loop stopping");
        }
        catch (Exception ex) {
            _logger.LogError("Error in RawCollector - {Error}", ex.Message);
        }
    }

    private async Task RestoreStateFsm(CancellationToken ct) {
        _logger.LogInformation("RestoreStateFsm");
        (Result res, StateFsmState? stateFsmState) = await _dbm.GetStateFsmState(ct);
        if (!res.Success) {
            _logger.LogError("RestoreStateFsm fatal error - {Error}", res.ErrMsg);
            throw new InvalidOperationException("RestoreStateFsm fatal error - " + res.ErrMsg);
        }

        if (stateFsmState is null) {
            _logger.LogWarning("RestoreStateFsm fatal error - could not restore state from database. New state created.");
            _stateFsm.State = new();
            return;
        }

        _logger.LogInformation("RestoreStateFsm - restored state from database");
        _stateFsm.State = stateFsmState;
    }

    private async Task RestoreInstrumentDirectory(CancellationToken ct) {
        _logger.LogInformation("RestoreInstrumentDirectory");
        (Result res, IReadOnlyList<InstrumentDto> instrumentList) = await _dbm.GetInstrumentList(ct);

        if (!res.Success) {
            _logger.LogError("RestoreInstrumentDirectory fatal error - {Error}", res.ErrMsg);
            throw new InvalidOperationException("RestoreInstrumentDirectory fatal error - " + res.ErrMsg);
        }

        if (instrumentList.Count == 0) {
            _logger.LogWarning("RestoreInstrumentDirectory - no instruments to restore, starting from empty");
            return;
        }

        _registry.InitializeDirectory(instrumentList);
        _logger.LogInformation("RestoreInstrumentDirectory - restored {Count} instruments", instrumentList.Count);
    }

    private static TimeSpan CalcIntervalMs(DateTime time1, DateTime? time2) {
        if (time2 is null)
            return TimeSpan.FromMilliseconds(1);
        TimeSpan? diff_ = time2 - time1;
        TimeSpan diff = diff_!.Value;
        if (diff < TimeSpan.FromMilliseconds(1))
            return TimeSpan.FromMilliseconds(1);
        return diff;
    }

    private async Task ProcessOutput(IList<StateFsmOutputItemBase> outputList, CancellationToken ct) {
        foreach (var outputItem in outputList) {
            switch (outputItem) {
                case FetchDirectory: await ProcessFetchDirectory(ct); break;
                case FetchInstrumentData fid: await ProcessFetchInstrumentData(fid, ct); break;
                case PersistState: await ProcessPersistState(ct); break;
                default: {
                    _logger.LogWarning("ProcessOutput - Unexpected output type encountered: {@Output}", outputItem);
                    break;
                }
            }
        }
    }

    private async Task ProcessFetchDirectory(CancellationToken ct) {
        _logger.LogInformation("ProcessFetchDirectory begin");

        Dictionary<string, InstrumentDtoByInstrumentNameMap> directory = await FetchInstrumentDirectory(ct); // Map from company symbol --> instrument symbol --> InstrumentDto

        IList<InstrumentDto> newInstrumentList = _registry.GetNewInstruments(directory);
        for (var i = 0; i < newInstrumentList.Count; i++)
            newInstrumentList[i] = newInstrumentList[i] with { InstrumentId = await _dbm.GetNextId64(ct) };

        IList<InstrumentDto> obsoletedInstrumentList = _registry.GetObsoletedInstruments(directory);
        foreach (var obsoletedInstrument in obsoletedInstrumentList)
            _registry.RemoveInstrument(obsoletedInstrument);

        _logger.LogInformation("ProcessFetchDirectory - new:{NumNewInstruments},obsolete:{NumObsoletedInstruments}",
            newInstrumentList.Count, obsoletedInstrumentList.Count);
        Result res = await _dbm.UpdateInstrumentList((IReadOnlyList<InstrumentDto>)newInstrumentList, (IReadOnlyList<InstrumentDto>)obsoletedInstrumentList, ct);

        if (res.Success)
            _logger.LogInformation("ProcessFetchDirectory - end. Success: {Success}", res.Success);
        else
            _logger.LogInformation("ProcessFetchDirectory - end. Success: {Success}. Error: {Error}", res.Success, res.ErrMsg);
    }

    private async Task<Dictionary<string, InstrumentDtoByInstrumentNameMap>> FetchInstrumentDirectory(CancellationToken ct) {
        var directory = new Dictionary<string, InstrumentDtoByInstrumentNameMap>();
        string[] letters = {
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "0-9"
        };

        try {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("accept", "application/json, text/javascript, */*; q=0.01");
            httpClient.DefaultRequestHeaders.Add("accept-language", "en-US,en;q=0.9");
            httpClient.DefaultRequestHeaders.Add("sec-fetch-dest", "empty");
            httpClient.DefaultRequestHeaders.Add("sec-fetch-mode", "cors");
            httpClient.DefaultRequestHeaders.Add("sec-fetch-site", "same-origin");
            httpClient.DefaultRequestHeaders.Add("x-requested-with", "XMLHttpRequest");
            httpClient.DefaultRequestHeaders.Add("cookie", "tmx_locale=en;");

            foreach (string curLetter in letters) {
                var request = new HttpRequestMessage {
                    RequestUri = new Uri($"https://www.tsx.com/json/company-directory/search/tsx/{curLetter}"),
                    Method = HttpMethod.Get,
                    Headers = {
                        { "referrer", "https://www.tsx.com/listings/listing-with-us/listed-company-directory?lang=en" },
                        { "referrerPolicy", "strict-origin-when-cross-origin" },
                        // { "body", "" },
                        { "mode", "cors" }
                    },
                    Content = null
                };

                _logger.LogInformation("FetchInstrumentDirectory - fetching letter:{Letter}", curLetter);

                using HttpResponseMessage response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) {
                    _logger.LogWarning("FetchInstrumentDirectory - letter:{Letter}, HTTP request failed with status code: {StatusCode}",
                        curLetter, response.StatusCode);
                    continue;
                }

                string json = await response.Content.ReadAsStringAsync(ct);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (!root.TryGetProperty("results", out JsonElement curLetterDirectory)) {
                    _logger.LogWarning("FetchInstrumentDirectory - letter:{Letter} - 'results' property not found", curLetter);
                    continue;
                }
                if (curLetterDirectory.ValueKind != JsonValueKind.Array) {
                    _logger.LogWarning("FetchInstrumentDirectory - letter:{Letter} - directory is not in array form", curLetter);
                    continue;
                }

                foreach (JsonElement company in curLetterDirectory.EnumerateArray()) {
                    if (!company.TryGetProperty("symbol", out JsonElement companySymbol)) {
                        _logger.LogWarning("FetchInstrumentDirectory - letter:{Letter},company:{@company} - no 'symbol' property", curLetter, company);
                        continue;
                    }
                    if (!company.TryGetProperty("name", out JsonElement companyName)) {
                        _logger.LogWarning("FetchInstrumentDirectory - letter:{Letter},company:{@company} - no 'name' property", curLetter, company);
                        continue;
                    }
                    if (!company.TryGetProperty("instruments", out JsonElement instruments)
                        || instruments.ValueKind != JsonValueKind.Array) {
                        _logger.LogWarning("FetchInstrumentDirectory - letter:{Letter},company:{@company} - no 'instruments' property, or not an array", curLetter, company);
                        continue;
                    }

                    foreach (JsonElement instrument in instruments.EnumerateArray()) {
                        if (!instrument.TryGetProperty("symbol", out JsonElement instrumentSymbol)) {
                            _logger.LogWarning("FetchInstrumentDirectory - letter:{Letter},company:{@company} - no instrument 'symbol' property", curLetter, company);
                            continue;
                        }
                        if (!instrument.TryGetProperty("name", out JsonElement instrumentName)) {
                            _logger.LogWarning("FetchInstrumentDirectory - letter:{Letter},company:{@company} - no instrument 'name' property", curLetter, company);
                            continue;
                        }

                        var dto = new InstrumentDto(
                            0,
                            Constants.TsxExchange,
                            companySymbol.GetString()!,
                            companyName.GetString()!,
                            instrumentSymbol.GetString()!,
                            instrumentName.GetString()!,
                            DateTimeOffset.UtcNow,
                            null);

                        if (dto.IsTsxPeferredShares) {
                            _logger.LogInformation("Instrument preferred shared, not adding: {Instrument}", dto);
                            continue;
                        }
                        if (dto.IsTsxWarrant) {
                            _logger.LogInformation("Instrument is a warrant, not adding: {Instrument}", dto);
                            continue;
                        }
                        if (dto.IsTsxCompanyBonds) {
                            _logger.LogInformation("Instrument is company bonds, not adding: {Instrument}", dto);
                            continue;
                        }
                        if (dto.IsTsxETF) {
                            _logger.LogInformation("Instrument is an ETF, not adding: {Instrument}", dto);
                            continue;
                        }
                        if (dto.IsTsxBmoMutualFund) {
                            _logger.LogInformation("Instrument is a BMO Mutual fund, not adding: {Instrument}", dto);
                            continue;
                        }
                        if (dto.IsPimcoMutualFund) {
                            _logger.LogInformation("Instrument is a PIMCO Mutual fund, not adding: {Instrument}", dto);
                            continue;
                        }
                        if (dto.IsTsxMutualFund) {
                            _logger.LogInformation("Instrument is a Mutual fund, not adding: {Instrument}", dto);
                            continue;
                        }

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
                        } else {
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

    private async Task ProcessFetchInstrumentData(FetchInstrumentData outputItem, CancellationToken ct) {
        _logger.LogInformation("ProcessFetchInstrumentData(company:{Company},instrument:{Instrument})",
            outputItem.CompanySymbol, outputItem.InstrumentSymbol);
        var insrumentKey = new InstrumentKey(outputItem.CompanySymbol, outputItem.InstrumentSymbol, outputItem.Exchange);
        var instrumentDto = _registry.GetInstrument(insrumentKey);
        if (instrumentDto is null) {
            _logger.LogWarning("ProcessFetchInstrumentData - Failed to find {Company} in the registry", insrumentKey);
            return;
        }

        // Fetch current raw data for the company from the raw database, if any
        (Result res, IReadOnlyList<InstrumentReportDto> existingRawFinancials)  = await _dbm.GetRawFinancialsByInstrumentId((long)instrumentDto.InstrumentId, ct);
        if (!res.Success)
            _logger.LogWarning("ProcessFetchInstrumentData - Failed to fetch existing raw reports from the database - Error:{ErrMsg}", res.ErrMsg);
        else
            _logger.LogInformation("ProcessFetchInstrumentData - got {NumRawReports} raw financial reports from the database", existingRawFinancials.Count);

        Result<TsxCompanyData> fetchSuccess = await GetRawFinancials(instrumentDto, ct);

        if (!fetchSuccess.Success) {
            _logger.LogWarning("ProcessFetchInstrumentData(company:{Company},instrument:{Instrument}) - Failed to get new raw financial data, aborting. {Err}",
                outputItem.CompanySymbol, outputItem.InstrumentSymbol, fetchSuccess.ErrMsg);
            return;
        }

        TsxCompanyData? newRawCompanyData = fetchSuccess.Data;
        if (newRawCompanyData is null) {
            _logger.LogWarning("ProcessFetchInstrumentData(company:{Company},instrument:{Instrument}) - Malformed new raw financial data, aborting.",
                outputItem.CompanySymbol, outputItem.InstrumentSymbol);
            return;
        }

        var deltaTool = new RawFinancialDeltaTool(_svp);
        RawFinancialsDelta delta = await deltaTool.TakeDelta(instrumentDto.InstrumentId, existingRawFinancials, newRawCompanyData, ct);

        _logger.LogInformation("ProcessFetchInstrumentData - Updating instrument reports. # shares {CurNumShares}, price per share: ${PricePerShare}",
            newRawCompanyData.CurNumShares, newRawCompanyData.PricePerShare);
        Result updateInstrumentRes = await _dbm.UpdateInstrumentReports(delta, ct);
        
        if (updateInstrumentRes.Success) {
            _logger.LogInformation("ProcessFetchInstrumentData - Updated instrument reports success");
        } else {
            _logger.LogWarning("ProcessFetchInstrumentData - Updated instrument reports failed with error {Error}", updateInstrumentRes.ErrMsg);
        }
    }

    private async Task<Result<TsxCompanyData>> GetRawFinancials(InstrumentDto instrumentDto, CancellationToken ct) {
        TsxCompanyProcessor companyProcessor = await TsxCompanyProcessor.Create(instrumentDto, _svp, ct);
        await companyProcessor.StartAsync(ct);
        return await companyProcessor.GetRawFinancials();
    }

    private async Task ProcessPersistState(CancellationToken ct) {
        _logger.LogInformation("ProcessPersistState begin");
        Result res = await _dbm.PersistStateFsmState(_stateFsm.State, ct);
        if (res.Success)
            _logger.LogInformation("ProcessPersistState success");
        else
            _logger.LogInformation("ProcessPersistState failed with error: {ErrMsg}", res.ErrMsg);
    }
}
