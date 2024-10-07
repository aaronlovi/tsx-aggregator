using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using tsx_aggregator.shared;
using static tsx_aggregator.shared.Constants;

namespace tsx_aggregator.models;

public class CompanyReportBuilder {
    private readonly ILogger _logger;
    private readonly CompanyReport _rpt;

    public CompanyReportBuilder(string instrumentSymbol, string instrumentName, string exchange, long numShares, ILogger logger) {
        _rpt = new(instrumentSymbol, instrumentName, exchange, numShares);
        _logger = logger;
    }

    public CompanyReportBuilder AddRawReport(CurrentInstrumentRawDataReportDto rawReport) {
        ReportTypes rptType = (ReportTypes)rawReport.ReportType;
        if (!rptType.IsValid()) {
            _logger.LogWarning("CompanyReportBuilder - unexpected report type: {ReportType}. Instrument: {rpt}",
                rptType, rawReport);
            return this;
        }

        ReportPeriodTypes rptPeriod = (ReportPeriodTypes)rawReport.ReportPeriodType;
        if (!rptPeriod.IsValid()) {
            _logger.LogWarning("CompanyReportBuilder - unexpected report period type: {ReportPeriodType}. Instrument: {rpt}",
                rptPeriod, rawReport);
            return this;
        }

        using JsonDocument doc = JsonDocument.Parse(rawReport.ReportJson);
        JsonElement root = doc.RootElement;
        var reportData = new RawReportDataMap();
        foreach (JsonProperty prop in root.EnumerateObject()) {
            if (prop.Value.ValueKind != JsonValueKind.Number)
                continue;
            reportData[prop.Name] = prop.Value.GetDecimal();
        }

        switch (rptType) {
            case ReportTypes.CashFlow:
                _rpt.AddCashFlowStatement(rptPeriod, rawReport.ReportDate, reportData);
                break;
            case ReportTypes.IncomeStatement:
                _rpt.AddIncomeStatement(rptPeriod, rawReport.ReportDate, reportData);
                break;
            case ReportTypes.BalanceSheet:
                _rpt.AddBalanceSheet(rptPeriod, rawReport.ReportDate, reportData);
                break;
            case ReportTypes.Undefined:
            default:
                _logger.LogWarning("CompanyReportBuilder - unexpected report type {ReportType}", rawReport.ReportType);
                break;
        }

        return this;
    }

    public CompanyReport Build() {
        var warnings = new List<string>();
        _rpt.ProcessReports(warnings);
        foreach (var warning in warnings)
            _logger.LogWarning("CompanyReportBuilder: {Warning}", warning);

        return _rpt;
    }
}
