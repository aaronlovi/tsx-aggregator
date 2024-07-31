using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;
using tsx_aggregator.Raw;
using tsx_aggregator.shared;

namespace tsx_aggregator;

internal partial class RawCollector : BackgroundService {

    /// <summary>
    /// This is one of the main functions of the Raw data collector.
    /// Fetches raw data for a single instrument
    /// Updates or inserts raw data for the instrument in the database
    /// </summary>
    private async Task ProcessFetchInstrumentData(FetchRawCollectorInstrumentDataOutput outputItem, CancellationToken ct) {
        _logger.LogInformation("ProcessFetchInstrumentData(company:{Company},instrument:{Instrument})",
            outputItem.CompanySymbol, outputItem.InstrumentSymbol);
        var insrumentKey = new InstrumentKey(outputItem.CompanySymbol, outputItem.InstrumentSymbol, outputItem.Exchange);
        var instrumentDto = _registry.GetInstrument(insrumentKey);
        if (instrumentDto is null) {
            _logger.LogWarning("ProcessFetchInstrumentData - Failed to find {Company} in the registry", insrumentKey);
            return;
        }

        // Fetch current raw data for the company from the raw database, if any
        (Result res, IReadOnlyList<CurrentInstrumentReportDto> existingRawFinancials) = await _dbm.GetRawFinancialsByInstrumentId(instrumentDto.InstrumentId, ct);
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
        }
        else {
            _logger.LogWarning("ProcessFetchInstrumentData - Updated instrument reports failed with error {Error}", updateInstrumentRes.ErrMsg);
        }
    }

    /// <summary>
    /// Fetches raw financial data for a given instrument.
    /// </summary>
    /// <param name="instrumentDto">The instrument for which to fetch the raw financial data.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A Result containing the raw financial data for the instrument, or an error message if the operation failed.</returns>
    private async Task<Result<TsxCompanyData>> GetRawFinancials(InstrumentDto instrumentDto, CancellationToken ct) {
        // Create a cancellation token that is linked to the provided cancellation token.
        // The provided cancellation token is expected to be the application stopping token, so
        // the linked token will be canceled when either the application is stopping, or we are done with the processor.
        using var cts = Utilities.CreateLinkedTokenSource(null, ct);

        using TsxCompanyProcessor companyProcessor = await TsxCompanyProcessor.Create(instrumentDto, _svp, cts.Token);
        await companyProcessor.StartAsync(cts.Token);
        Result<TsxCompanyData> results = await companyProcessor.GetRawFinancials();

        // Cancel the processor
        cts.Cancel();

        return results;
    }
}
