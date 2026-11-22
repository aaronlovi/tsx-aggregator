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

    private static readonly DateTime Now = new(2026, 11, 22, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NextCheck_DueOrUnknown_IsNow() {
        _ = QuoteService.DelayUntilNextCheck(null, Now).Should().Be(TimeSpan.Zero);
        _ = QuoteService.DelayUntilNextCheck(Now.AddMinutes(-5), Now).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void NextCheck_NormalCycle_WaitsUntilTheFetchTime() {
        _ = QuoteService.DelayUntilNextCheck(Now.AddMinutes(90), Now).Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void NextCheck_FarFuture_WaitsAtMostADayAndTaskDelayAcceptsIt() {
        // A saved fetch time beyond Task.Delay's limit (~49.7 days) used to throw and stop the whole host.
        TimeSpan delay = QuoteService.DelayUntilNextCheck(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc), Now);

        _ = delay.Should().Be(TimeSpan.FromDays(1));
        Action schedule = () => _ = System.Threading.Tasks.Task.Delay(delay, new System.Threading.CancellationToken(canceled: true));
        _ = schedule.Should().NotThrow();
    }
}
