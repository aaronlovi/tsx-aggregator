using System;
using System.Collections.Generic;
using tsx_aggregator.shared;

namespace tsx_aggregator.models;

public record RawReportConsistencyMapKey(int ReportType, int ReportPeriodType, DateOnly ReportDate);

public record RawReportConsistencyMapValue(long InstrumentReportid, bool IsCurrent, bool CheckManually);

public class RawReportConsistencyMap : Dictionary<RawReportConsistencyMapKey, List<RawReportConsistencyMapValue>> {
    public RawReportConsistencyMapKey? BuildMap(
        RawInstrumentReportsToKeepAndIgnoreDto dto, IReadOnlyCollection<InstrumentRawDataReportDto> instrumentReports) {

        RawReportConsistencyMapKey? mainKey = null;

        foreach (InstrumentRawDataReportDto report in instrumentReports) {
            var key = new RawReportConsistencyMapKey(report.ReportType, report.ReportPeriodType, report.ReportDate);
            var value = new RawReportConsistencyMapValue(report.InstrumentReportId, report.IsCurrent, report.CheckManually);

            if (!TryGetValue(key, out List<RawReportConsistencyMapValue>? values))
                this[key] = values = new List<RawReportConsistencyMapValue>();
            values.Add(value);

            if (report.InstrumentReportId == dto.ReportIdToKeep)
                mainKey = key;
        }

        return mainKey;
    }

    // Ensure that the report to keep, and all of the reports to ignore are:
    // - in the list of reports
    // - are of the same report type, reporting period, and report date
    // - are current
    // And that there are no other current raw reports with the same report type, reporting period, and report date
    public Result EnsureRequestIsConsistent(RawInstrumentReportsToKeepAndIgnoreDto dto, RawReportConsistencyMapKey mainKey) {
        bool foundValidReportIdToKeep = false;
        var instrumentReportIdsToIgnore = new HashSet<long>(dto.ReportIdsToIgnore);

        if (!TryGetValue(mainKey, out List<RawReportConsistencyMapValue>? values))
            return Result.SetFailure("Report to keep not found");

        foreach (RawReportConsistencyMapValue val in values) {
            if (val.InstrumentReportid == dto.ReportIdToKeep) {
                if (val.IsCurrent)
                    foundValidReportIdToKeep = true;
                else
                    return Result.SetFailure($"Report id to keep {val.InstrumentReportid} is not current");
            }
            else {
                if (!instrumentReportIdsToIgnore.Contains(val.InstrumentReportid) && val.IsCurrent)
                    return Result.SetFailure($"Found current report id {val.InstrumentReportid} that is neither to keep nor ignore");

                if (instrumentReportIdsToIgnore.Contains(val.InstrumentReportid) && !val.IsCurrent)
                    return Result.SetFailure($"Found non-current report id {val.InstrumentReportid} that is to ignore");

                instrumentReportIdsToIgnore.Remove(val.InstrumentReportid);
            }
        }

        if (!foundValidReportIdToKeep)
            return Result.SetFailure($"Report id to keep {dto.ReportIdToKeep} not found");

        if (instrumentReportIdsToIgnore.Count > 0) {
            string logStr = LogUtils.GetLogStr(instrumentReportIdsToIgnore);
            return Result.SetFailure($"Some report ids {logStr} to ignore were not found");
        }

        return Result.SUCCESS;
    }
}
