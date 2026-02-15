export class ScoreDistributionItem {
    score: number;
    count: number;

    constructor(score: number, count: number) {
        this.score = score;
        this.count = count;
    }
}

export class ScoreCategoryStats {
    score: number;
    count: number;
    sumMarketCap: number;
    meanMarketCap: number;
    medianMarketCap: number;
    meanReturnFromCashFlow: number;
    medianReturnFromCashFlow: number;
    meanReturnFromOwnerEarnings: number;
    medianReturnFromOwnerEarnings: number;

    constructor(
        score: number,
        count: number,
        sumMarketCap: number,
        meanMarketCap: number,
        medianMarketCap: number,
        meanReturnFromCashFlow: number,
        medianReturnFromCashFlow: number,
        meanReturnFromOwnerEarnings: number,
        medianReturnFromOwnerEarnings: number
    ) {
        this.score = score;
        this.count = count;
        this.sumMarketCap = sumMarketCap;
        this.meanMarketCap = meanMarketCap;
        this.medianMarketCap = medianMarketCap;
        this.meanReturnFromCashFlow = meanReturnFromCashFlow;
        this.medianReturnFromCashFlow = medianReturnFromCashFlow;
        this.meanReturnFromOwnerEarnings = meanReturnFromOwnerEarnings;
        this.medianReturnFromOwnerEarnings = medianReturnFromOwnerEarnings;
    }
}

export class DashboardAggregates {
    totalCompanies: number;
    companiesWithPriceData: number;
    companiesWithoutPriceData: number;
    companiesPassingAllChecks: number;
    averageEstimatedReturn_FromCashFlow: number;
    averageEstimatedReturn_FromOwnerEarnings: number;
    medianEstimatedReturn_FromCashFlow: number;
    medianEstimatedReturn_FromOwnerEarnings: number;
    totalMarketCap: number;
    scoreDistribution: ScoreDistributionItem[];
    scoreCategoryStatistics: ScoreCategoryStats[];

    constructor(
        totalCompanies: number,
        companiesWithPriceData: number,
        companiesWithoutPriceData: number,
        companiesPassingAllChecks: number,
        averageEstimatedReturn_FromCashFlow: number,
        averageEstimatedReturn_FromOwnerEarnings: number,
        medianEstimatedReturn_FromCashFlow: number,
        medianEstimatedReturn_FromOwnerEarnings: number,
        totalMarketCap: number,
        scoreDistribution: any[],
        scoreCategoryStatistics: any[]
    ) {
        this.totalCompanies = totalCompanies;
        this.companiesWithPriceData = companiesWithPriceData;
        this.companiesWithoutPriceData = companiesWithoutPriceData;
        this.companiesPassingAllChecks = companiesPassingAllChecks;
        this.averageEstimatedReturn_FromCashFlow = averageEstimatedReturn_FromCashFlow;
        this.averageEstimatedReturn_FromOwnerEarnings = averageEstimatedReturn_FromOwnerEarnings;
        this.medianEstimatedReturn_FromCashFlow = medianEstimatedReturn_FromCashFlow;
        this.medianEstimatedReturn_FromOwnerEarnings = medianEstimatedReturn_FromOwnerEarnings;
        this.totalMarketCap = totalMarketCap;
        this.scoreDistribution = scoreDistribution.map(
            sd => new ScoreDistributionItem(sd.score, sd.count)
        );
        this.scoreCategoryStatistics = (scoreCategoryStatistics || []).map(
            s => new ScoreCategoryStats(
                s.score, s.count, s.sumMarketCap, s.meanMarketCap, s.medianMarketCap,
                s.meanReturnFromCashFlow, s.medianReturnFromCashFlow, s.meanReturnFromOwnerEarnings, s.medianReturnFromOwnerEarnings
            )
        );
    }
}

export class RawReportCount {
    reportType: number;
    reportTypeName: string;
    count: number;

    constructor(reportType: number, reportTypeName: string, count: number) {
        this.reportType = reportType;
        this.reportTypeName = reportTypeName;
        this.count = count;
    }
}

export class DashboardStats {
    totalActiveInstruments: number;
    totalObsoletedInstruments: number;
    instrumentsWithProcessedReports: number;
    instrumentsWithoutProcessedReports: number;
    mostRecentRawIngestion: Date | null;
    mostRecentAggregation: Date | null;
    unprocessedEventCount: number;
    rawReportCounts: RawReportCount[];
    nextFetchDirectoryTime: Date | null;
    nextFetchInstrumentDataTime: Date | null;
    nextFetchQuotesTime: Date | null;
    nextAggregatorCycleTime: Date | null;

    constructor(
        totalActiveInstruments: number,
        totalObsoletedInstruments: number,
        instrumentsWithProcessedReports: number,
        instrumentsWithoutProcessedReports: number,
        mostRecentRawIngestion: string | null,
        mostRecentAggregation: string | null,
        unprocessedEventCount: number,
        rawReportCounts: any[],
        nextFetchDirectoryTime: string | null,
        nextFetchInstrumentDataTime: string | null,
        nextFetchQuotesTime: string | null,
        nextAggregatorCycleTime: string | null
    ) {
        this.totalActiveInstruments = totalActiveInstruments;
        this.totalObsoletedInstruments = totalObsoletedInstruments;
        this.instrumentsWithProcessedReports = instrumentsWithProcessedReports;
        this.instrumentsWithoutProcessedReports = instrumentsWithoutProcessedReports;
        this.mostRecentRawIngestion = mostRecentRawIngestion ? new Date(mostRecentRawIngestion) : null;
        this.mostRecentAggregation = mostRecentAggregation ? new Date(mostRecentAggregation) : null;
        this.unprocessedEventCount = unprocessedEventCount;
        this.rawReportCounts = rawReportCounts.map(
            rc => new RawReportCount(rc.reportType, rc.reportTypeName, rc.count)
        );
        this.nextFetchDirectoryTime = nextFetchDirectoryTime ? new Date(nextFetchDirectoryTime) : null;
        this.nextFetchInstrumentDataTime = nextFetchInstrumentDataTime ? new Date(nextFetchInstrumentDataTime) : null;
        this.nextFetchQuotesTime = nextFetchQuotesTime ? new Date(nextFetchQuotesTime) : null;
        this.nextAggregatorCycleTime = nextAggregatorCycleTime ? new Date(nextAggregatorCycleTime) : null;
    }
}
