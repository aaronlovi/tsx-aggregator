using System;
using FluentAssertions;
using tsx_aggregator.Services;

namespace tsx_aggregator.tests;

/// <summary>The QuoteService heartbeat's description of the next quote fetch.</summary>
public class QuoteServiceHeartbeatTests {
    [Fact]
    public void NextFetchQuotesTime_NotLoaded_SaysSoInsteadOfAFarFutureDate() {
        // Before the saved state is loaded (or if loading failed) there is no fetch time. It used to log 12/31/9999.
        string text = QuoteService.DescribeNextFetchQuotesTime(null);

        _ = text.Should().Be("not known (saved state not loaded)");
    }

    [Fact]
    public void NextFetchQuotesTime_Known_IsShownInUtc() {
        string text = QuoteService.DescribeNextFetchQuotesTime(new DateTime(2026, 11, 22, 1, 13, 29, DateTimeKind.Utc));

        _ = text.Should().Be("2026-11-22 01:13:29Z");
    }
}
