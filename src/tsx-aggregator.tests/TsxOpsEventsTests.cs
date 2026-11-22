using System;
using FluentAssertions;
using tsx_ops.Events;

namespace tsx_aggregator.tests;

public class TsxOpsEventsTests {
    [Fact]
    public void Format_ShowsPendingAndStuckByTypeWithTotals() {
        string text = EventQueue.Format([
            new EventQueueRow(1, 3, 0, new DateTimeOffset(2026, 11, 21, 18, 13, 0, TimeSpan.Zero)),
            new EventQueueRow(4, 2, 1, new DateTimeOffset(2026, 11, 22, 1, 5, 0, TimeSpan.Zero)),
        ]);

        _ = text.Should().Be("""
            Event type        Pending  Stuck  Oldest pending (UTC)
            NewListedCompany        3      0  2026-11-21 18:13
            RawDataChanged          2      1  2026-11-22 01:05
            Total                   5      1
            Stuck: RawDataChanged for a delisted instrument or one without a current price row; the Aggregator skips these.

            """.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Format_EmptyQueue_ShowsZeroTotal() {
        _ = EventQueue.Format([]).Should().Contain("Total").And.NotContain("Stuck:");
    }
}
