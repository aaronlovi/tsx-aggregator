using FluentAssertions;
using tsx_aggregator.models;

namespace tsx_aggregator.tests;

public class RawFinancialsDeltaTests {
    [Fact]
    public void RawFinancialsDelta_CanHoldItemsInAllThreeLists() {
        var delta = new RawFinancialsDelta(instrumentId: 1, numShares: 100, pricePerShare: 10.0m);

        var reportDto = TestDataFactory.CreateCurrentInstrumentReportDto(reportJson: "{\"A\": 1}");
        delta.InstrumentReportsToInsert.Add(reportDto);
        delta.InstrumentReportsToObsolete.Add(reportDto);
        delta.InstrumentReportsToUpdate.Add(new ReportUpdate(999, "{\"A\": 2}"));

        _ = delta.InstrumentReportsToInsert.Count.Should().Be(1);
        _ = delta.InstrumentReportsToObsolete.Count.Should().Be(1);
        _ = delta.InstrumentReportsToUpdate.Count.Should().Be(1);
    }

    [Fact]
    public void RawFinancialsDelta_CopyConstructorCopiesAllThreeLists() {
        var original = new RawFinancialsDelta(instrumentId: 1, numShares: 100, pricePerShare: 10.0m);

        var reportDto = TestDataFactory.CreateCurrentInstrumentReportDto(reportJson: "{\"A\": 1}");
        original.InstrumentReportsToInsert.Add(reportDto);
        original.InstrumentReportsToObsolete.Add(reportDto);
        original.InstrumentReportsToUpdate.Add(new ReportUpdate(999, "{\"A\": 2}"));

        var copy = new RawFinancialsDelta(original);

        _ = copy.InstrumentReportsToInsert.Count.Should().Be(1);
        _ = copy.InstrumentReportsToObsolete.Count.Should().Be(1);
        _ = copy.InstrumentReportsToUpdate.Count.Should().Be(1);
    }
}
