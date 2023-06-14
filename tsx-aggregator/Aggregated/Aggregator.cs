using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;
using tsx_aggregator.shared;
using static tsx_aggregator.shared.Constants;

namespace tsx_aggregator;

public class Aggregator : BackgroundService {
    private readonly ILogger _logger;
    private readonly IServiceProvider _svp;
    private readonly IDbmService _dbm;

    public Aggregator(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<Aggregator>>();
        _svp = svp;
        _dbm = svp.GetRequiredService<IDbmService>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _ = StartHeartbeat(_svp, stoppingToken); // Fire and forget

        while (!stoppingToken.IsCancellationRequested) {
            (Result res, InstrumentEventDto? instrumentEvt) = await _dbm.GetNextInstrumentEvent(stoppingToken);
            if (res.Success && instrumentEvt is not null) {
                _logger.LogInformation("Aggregator: Found instrument event {@InstrumentEvent}", instrumentEvt);
                await ProcessInstrumentEvent(instrumentEvt, stoppingToken);
            } else {
                // Don't smash the database unnecessarily. Wait a bit for the next incoming event.
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ProcessInstrumentEvent(InstrumentEventDto instrumentEvt, CancellationToken ct) {
        _logger.LogInformation("Found instrument event: {InstrumentEvent}", instrumentEvt);

        CompanyEventTypes eventType = instrumentEvt.EventType.ToCompanyEventType();
        switch (eventType) {
            case CompanyEventTypes.NewListedCompany:
                await ProcessNewListedCompanyEvent(instrumentEvt, ct);
                break;
            case CompanyEventTypes.UpdatedListedCompany:
                break;
            case CompanyEventTypes.ObsoletedListedCompany:
                break;
            case CompanyEventTypes.RawDataChanged:
                await ProcessCompanyDataChangedEvent(instrumentEvt, ct);
                break;
            case CompanyEventTypes.Undefined:
            default:
                break;
        }
    }

    private async Task ProcessNewListedCompanyEvent(InstrumentEventDto instrumentEvt, CancellationToken ct) {
        // Mark the event as processed. Nothing to do here for now.
        InstrumentEventDto dto = instrumentEvt with { IsProcessed = true };
        var res = await _dbm.MarkInstrumentEventAsProcessed(dto, ct);
        if (!res.Success)
            _logger.LogWarning("ProcessNewListedCompanyEvent - unexpected failed to mark instrument event as processed {Error}", res.ErrMsg);
    }

    private async Task ProcessCompanyDataChangedEvent(InstrumentEventDto instrumentEvt, CancellationToken ct) {
        (Result res, IReadOnlyList<InstrumentReportDto> rawReports) = await _dbm.GetCurrentInstrumentReports(instrumentEvt.InstrumentId, ct);
        if (!res.Success || rawReports.Count == 0) {
            _logger.LogWarning("ProcessCompanyDataChangedEvent - unexpected no company data changed ({DbResult},{NumReports})",
                res.Success, rawReports.Count);
            return;
        }

        var companyReportBuilder = new CompanyReportBuilder(
            instrumentEvt.InstrumentSymbol,
            instrumentEvt.InstrumentName,
            instrumentEvt.Exchange,
            instrumentEvt.NumShares,
            _logger);

        foreach (InstrumentReportDto rpt in rawReports)
            companyReportBuilder.AddRawReport(rpt);

        CompanyReport companyReport = companyReportBuilder.Build();

        string serializedReport = JsonSerializer.Serialize(companyReport);
        var processedReportDto = new ProcessedInstrumentReportDto(instrumentEvt.InstrumentId, serializedReport, DateTimeOffset.UtcNow, null);
        res = await _dbm.InsertProcessedCompanyReport(processedReportDto, ct);
        if (!res.Success)
            _logger.LogWarning("ProcessCompanyDataChangedEvent - unexpected failed to insert processed company report: {Error}", res.ErrMsg);
    }

    private static async Task StartHeartbeat(IServiceProvider svp, CancellationToken ct) {
        ILogger logger = svp.GetRequiredService<ILogger<Aggregator>>();
        while (!ct.IsCancellationRequested) {
            logger.LogInformation("Aggregator heartbeat");
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}
