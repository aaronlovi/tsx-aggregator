using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;
using tsx_aggregator.shared;

namespace tsx_aggregator;

internal class RawFinancialDeltaTool {
    private readonly ILogger<RawFinancialDeltaTool> _logger;
    private readonly IDbmService _dbm;

    internal RawFinancialDeltaTool(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<RawFinancialDeltaTool>>();
        _dbm = svp.GetRequiredService<IDbmService>();
    }

    public async Task<RawFinancialsDelta> TakeDelta(
        long instrumentId,
        IReadOnlyList<CurrentInstrumentReportDto> existingRawFinancials,
        TsxCompanyData newRawCompanyData,
        CancellationToken ct) {
        var retVal = new RawFinancialsDelta(instrumentId, newRawCompanyData.CurNumShares, newRawCompanyData.PricePerShare);

        var existingRawAnnualBalanceSheets = new List<CurrentInstrumentReportDto>();
        var existingRawAnnualCashFlowReports = new List<CurrentInstrumentReportDto>();
        var existingRawAnnualIncomeStatements = new List<CurrentInstrumentReportDto>();
        var existingRawQuarterlyBalanceSheets = new List<CurrentInstrumentReportDto>();
        var existingRawQuarterlyCashFlowReports = new List<CurrentInstrumentReportDto>();
        var existingRawQuarterlyIncomeStatements = new List<CurrentInstrumentReportDto>();
        foreach (CurrentInstrumentReportDto f in existingRawFinancials) {
            if (f.ReportType == (int)Constants.ReportTypes.BalanceSheet && f.ReportPeriodType == (int)Constants.ReportPeriodTypes.Annual)
                existingRawAnnualBalanceSheets.Add(f);
            else if (f.ReportType == (int)Constants.ReportTypes.CashFlow && f.ReportPeriodType == (int)Constants.ReportPeriodTypes.Annual)
                existingRawAnnualCashFlowReports.Add(f);
            else if (f.ReportType == (int)Constants.ReportTypes.IncomeStatement && f.ReportPeriodType == (int)Constants.ReportPeriodTypes.Annual)
                existingRawAnnualIncomeStatements.Add(f);
            else if (f.ReportType == (int)Constants.ReportTypes.BalanceSheet && f.ReportPeriodType == (int)Constants.ReportPeriodTypes.Quarterly)
                existingRawQuarterlyBalanceSheets.Add(f);
            else if (f.ReportType == (int)Constants.ReportTypes.CashFlow && f.ReportPeriodType == (int)Constants.ReportPeriodTypes.Quarterly)
                existingRawQuarterlyCashFlowReports.Add(f);
            else if (f.ReportType == (int)Constants.ReportTypes.IncomeStatement && f.ReportPeriodType == (int)Constants.ReportPeriodTypes.Quarterly)
                existingRawQuarterlyIncomeStatements.Add(f);
        }

        await TakeDeltaCore(instrumentId, newRawCompanyData.AnnualRawBalanceSheets, existingRawAnnualBalanceSheets, Constants.ReportTypes.BalanceSheet, Constants.ReportPeriodTypes.Annual, retVal, ct);
        await TakeDeltaCore(instrumentId, newRawCompanyData.AnnualRawCashFlowReports, existingRawAnnualCashFlowReports, Constants.ReportTypes.CashFlow, Constants.ReportPeriodTypes.Annual, retVal, ct);
        await TakeDeltaCore(instrumentId, newRawCompanyData.AnnualRawIncomeStatements, existingRawAnnualIncomeStatements, Constants.ReportTypes.IncomeStatement, Constants.ReportPeriodTypes.Annual, retVal, ct);
        await TakeDeltaCore(instrumentId, newRawCompanyData.QuarterlyRawBalanceSheets, existingRawQuarterlyBalanceSheets, Constants.ReportTypes.BalanceSheet, Constants.ReportPeriodTypes.Quarterly, retVal, ct);
        await TakeDeltaCore(instrumentId, newRawCompanyData.QuarterlyRawCashFlowReports, existingRawQuarterlyCashFlowReports, Constants.ReportTypes.CashFlow, Constants.ReportPeriodTypes.Quarterly, retVal, ct);
        await TakeDeltaCore(instrumentId, newRawCompanyData.QuarterlyRawIncomeStatements, existingRawQuarterlyIncomeStatements, Constants.ReportTypes.IncomeStatement, Constants.ReportPeriodTypes.Quarterly, retVal, ct);

        return retVal;
    }

    private async Task TakeDeltaCore(
        long instrumentId,
        IList<RawReportDataMap> newRawReportList,
        IReadOnlyList<CurrentInstrumentReportDto> existingRawReportList,
        Constants.ReportTypes reportType,
        Constants.ReportPeriodTypes reportPeriod,
        RawFinancialsDelta rawFinancialsDelta,
        CancellationToken ct) {
        
        foreach (RawReportDataMap newRawReport in newRawReportList) {
            if (newRawReport.ReportDate is null) {
                _logger.LogWarning("takeDeltaCore:{ReportType},{ReportPeriod} - cannot use new raw report because it is missing report date: {@NewRawReport}",
                    reportType, reportPeriod, newRawReport);
                continue;
            }

            if (!newRawReport.IsValid) {
                _logger.LogWarning("takeDeltaCore:{ReportType},{ReportPeriod} - cannot use new raw report because it was invalid: {@NewRawReport}",
                    reportType, reportPeriod, newRawReport);
                continue;
            }

            List<CurrentInstrumentReportDto> existingReportDtoRows = reportPeriod == Constants.ReportPeriodTypes.Annual
                ? existingRawReportList.Where(rpt => rpt.ReportDate.Year == newRawReport.ReportDate.Value.Year).ToList()
                : existingRawReportList.Where(rpt => {
                    var existingReportQuarter = DateQuarter.FromDate(rpt.ReportDate.ToDateTimeUtc());
                    var newReportQuarter = DateQuarter.FromDate(newRawReport.ReportDate.Value.ToDateTimeUtc());
                    return existingReportQuarter == newReportQuarter;
                }).ToList();

            if (existingReportDtoRows.Count > 0) {
                // Found a matching existing report. Check for field-by-field equivalence
                CurrentInstrumentReportDto existingReportDto = existingReportDtoRows[0];
                using JsonDocument existingReportJsonObj = JsonDocument.Parse(existingReportDto.ReportJson);
                if (!newRawReport.IsEqual(existingReportJsonObj)) {
                    rawFinancialsDelta.InstrumentReportsToInsert.Add(new CurrentInstrumentReportDto(
                        InstrumentReportId: (long)await _dbm.GetNextId64(ct),
                        InstrumentId: (long)instrumentId,
                        ReportType: (int)reportType,
                        ReportPeriodType: (int)reportPeriod,
                        ReportJson: newRawReport.AsJsonString(),
                        ReportDate: newRawReport.ReportDate.Value)); ;
                    rawFinancialsDelta.InstrumentReportsToObsolete.Add(existingReportDto);
                }
            } else {
                // No matching existing report rows. Just insert the new report we found
                rawFinancialsDelta.InstrumentReportsToInsert.Add(new CurrentInstrumentReportDto(
                    InstrumentReportId: (long)await _dbm.GetNextId64(ct),
                    InstrumentId: (long)instrumentId,
                    ReportType: (int)reportType,
                    ReportPeriodType: (int)reportPeriod,
                    ReportJson: newRawReport.AsJsonString(),
                    ReportDate: newRawReport.ReportDate.Value));
            }
        }
    }
}
