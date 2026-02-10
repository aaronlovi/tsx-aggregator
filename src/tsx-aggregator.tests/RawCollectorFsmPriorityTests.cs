using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using tsx_aggregator.models;
using tsx_aggregator.Raw;

namespace tsx_aggregator.tests;

public class RawCollectorFsmPriorityTests {
    private static Registry CreateRegistryWithInstruments(params (string CompanySymbol, string InstrumentSymbol, string Exchange)[] instruments) {
        var registry = new Registry();
        var list = new List<InstrumentDto>();
        long id = 1;
        foreach (var (cs, ins, ex) in instruments) {
            list.Add(new InstrumentDto(id++, ex, cs, cs + " Inc.", ins, ins + " Common", DateTimeOffset.UtcNow, null));
        }
        registry.InitializeDirectory(list);
        return registry;
    }

    private static RawCollectorFsm CreateFsm(Registry registry, DateTime? curTime = null) {
        var logger = NullLoggerFactory.Instance.CreateLogger("RawCollectorFsm");
        return new RawCollectorFsm(logger, curTime ?? DateTime.UtcNow, registry);
    }

    [Fact]
    public void ProcessUpdateTime_UsesPriorityKeyInsteadOfRoundRobin() {
        // Arrange
        var registry = CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("BBB", "BBB", "TSX"),
            ("CCC", "CCC", "TSX"));
        var fsm = CreateFsm(registry);
        registry.SetPriorityCompanies(new[] { "CCC" });

        // Trigger: set NextFetchInstrumentDataTime to the past
        fsm.NextFetchInstrumentDataTime = null;

        var output = new RawCollectorFsmOutputs();
        var input = new RawCollectorTimeoutInput(1, null, DateTime.UtcNow);

        // Act
        fsm.Update(input, DateTime.UtcNow, output);

        // Assert
        var fetchOutput = output.OutputList.OfType<FetchRawCollectorInstrumentDataOutput>().Single();
        fetchOutput.CompanySymbol.Should().Be("CCC");
    }

    [Fact]
    public void ProcessUpdateTime_DoesNotUpdatePrevInstrumentKeyForPriorityFetch() {
        // Arrange
        var registry = CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("BBB", "BBB", "TSX"),
            ("CCC", "CCC", "TSX"));
        var fsm = CreateFsm(registry);

        // Set PrevInstrumentKey to AAA (so round-robin would go to BBB next)
        fsm.PrevInstrumentKey = new InstrumentKey("AAA", "AAA", "TSX");
        registry.SetPriorityCompanies(new[] { "CCC" });

        fsm.NextFetchInstrumentDataTime = null;
        var output = new RawCollectorFsmOutputs();
        var input = new RawCollectorTimeoutInput(1, null, DateTime.UtcNow);

        // Act
        fsm.Update(input, DateTime.UtcNow, output);

        // Assert: PrevInstrumentKey should still be AAA, not CCC
        fsm.PrevInstrumentKey.CompanySymbol.Should().Be("AAA");

        // And the output should be for CCC (priority)
        var fetchOutput = output.OutputList.OfType<FetchRawCollectorInstrumentDataOutput>().Single();
        fetchOutput.CompanySymbol.Should().Be("CCC");
    }

    [Fact]
    public void ProcessUpdateTime_ResumesRoundRobinWhenPriorityQueueEmpty() {
        // Arrange
        var registry = CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("BBB", "BBB", "TSX"),
            ("CCC", "CCC", "TSX"));
        var fsm = CreateFsm(registry);

        // PrevInstrumentKey is AAA, so round-robin should go to BBB
        fsm.PrevInstrumentKey = new InstrumentKey("AAA", "AAA", "TSX");

        // No priority companies set
        fsm.NextFetchInstrumentDataTime = null;
        var output = new RawCollectorFsmOutputs();
        var input = new RawCollectorTimeoutInput(1, null, DateTime.UtcNow);

        // Act
        fsm.Update(input, DateTime.UtcNow, output);

        // Assert: should use round-robin (BBB)
        var fetchOutput = output.OutputList.OfType<FetchRawCollectorInstrumentDataOutput>().Single();
        fetchOutput.CompanySymbol.Should().Be("BBB");
        fsm.PrevInstrumentKey.CompanySymbol.Should().Be("BBB");
    }

    [Fact]
    public void ProcessUpdateTime_SkipsUnknownPrioritySymbolAndUsesNextValid() {
        // Arrange
        var registry = CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("BBB", "BBB", "TSX"));
        var fsm = CreateFsm(registry);

        registry.SetPriorityCompanies(new[] { "UNKNOWN", "BBB" });

        fsm.NextFetchInstrumentDataTime = null;
        var output = new RawCollectorFsmOutputs();
        var input = new RawCollectorTimeoutInput(1, null, DateTime.UtcNow);

        // Act
        fsm.Update(input, DateTime.UtcNow, output);

        // Assert
        var fetchOutput = output.OutputList.OfType<FetchRawCollectorInstrumentDataOutput>().Single();
        fetchOutput.CompanySymbol.Should().Be("BBB");
    }

    [Fact]
    public void Update_HandlesPriorityInputTypeWithoutWarning() {
        // Arrange: Verify that SetPriorityCompaniesInput falls through to ProcessUpdateTime
        var registry = CreateRegistryWithInstruments(("AAA", "AAA", "TSX"));
        var fsm = CreateFsm(registry);

        fsm.NextFetchInstrumentDataTime = null;
        var output = new RawCollectorFsmOutputs();
        var input = new RawCollectorSetPriorityCompaniesInput(1, new[] { "AAA" }, null);

        // Act
        fsm.Update(input, DateTime.UtcNow, output);

        // Assert: Should produce a fetch output (ProcessUpdateTime was called)
        output.OutputList.OfType<FetchRawCollectorInstrumentDataOutput>().Should().HaveCount(1);
    }

    [Fact]
    public void Update_HandlesGetPriorityInputTypeWithoutWarning() {
        // Arrange: Verify that GetPriorityCompaniesInput falls through to ProcessUpdateTime
        var registry = CreateRegistryWithInstruments(("AAA", "AAA", "TSX"));
        var fsm = CreateFsm(registry);

        fsm.NextFetchInstrumentDataTime = null;
        var output = new RawCollectorFsmOutputs();
        var input = new RawCollectorGetPriorityCompaniesInput(1, null);

        // Act
        fsm.Update(input, DateTime.UtcNow, output);

        // Assert: Should produce a fetch output (ProcessUpdateTime was called)
        output.OutputList.OfType<FetchRawCollectorInstrumentDataOutput>().Should().HaveCount(1);
    }
}
