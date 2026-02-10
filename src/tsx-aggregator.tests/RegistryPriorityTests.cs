using System;
using System.Collections.Generic;
using FluentAssertions;
using tsx_aggregator.models;
using tsx_aggregator.Raw;

namespace tsx_aggregator.tests;

public class RegistryPriorityTests {
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

    [Fact]
    public void SetPriorityCompanies_PopulatesQueue() {
        // Arrange
        var registry = CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("BBB", "BBB", "TSX"),
            ("CCC", "CCC", "TSX"));

        // Act
        int validCount = registry.SetPriorityCompanies(new[] { "CCC", "AAA" });

        // Assert
        validCount.Should().Be(2);
        registry.GetPriorityCompanySymbols().Should().Equal("CCC", "AAA");
    }

    [Fact]
    public void SetPriorityCompanies_DeduplicatesSymbols() {
        // Arrange
        var registry = CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("BBB", "BBB", "TSX"));

        // Act
        int validCount = registry.SetPriorityCompanies(new[] { "AAA", "BBB", "AAA", "BBB" });

        // Assert
        validCount.Should().Be(2);
        registry.GetPriorityCompanySymbols().Should().Equal("AAA", "BBB");
    }

    [Fact]
    public void TryDequeueNextPriorityInstrumentKey_ReturnsCorrectKey() {
        // Arrange
        var registry = CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("BBB", "BBB", "TSX"),
            ("CCC", "CCC", "TSX"));
        registry.SetPriorityCompanies(new[] { "BBB" });

        // Act
        bool found = registry.TryDequeueNextPriorityInstrumentKey(out var key);

        // Assert
        found.Should().BeTrue();
        key.Should().NotBeNull();
        key!.CompanySymbol.Should().Be("BBB");
        key.InstrumentSymbol.Should().Be("BBB");
        key.Exchange.Should().Be("TSX");
    }

    [Fact]
    public void TryDequeueNextPriorityInstrumentKey_SkipsUnknownSymbols() {
        // Arrange
        var registry = CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("CCC", "CCC", "TSX"));
        registry.SetPriorityCompanies(new[] { "UNKNOWN", "CCC" });

        // Act
        bool found = registry.TryDequeueNextPriorityInstrumentKey(out var key);

        // Assert
        found.Should().BeTrue();
        key!.CompanySymbol.Should().Be("CCC");
    }

    [Fact]
    public void TryDequeueNextPriorityInstrumentKey_ReturnsFalseWhenEmpty() {
        // Arrange
        var registry = CreateRegistryWithInstruments(("AAA", "AAA", "TSX"));

        // Act
        bool found = registry.TryDequeueNextPriorityInstrumentKey(out var key);

        // Assert
        found.Should().BeFalse();
        key.Should().BeNull();
    }

    [Fact]
    public void TryDequeueNextPriorityInstrumentKey_ReturnsFalseWhenAllSymbolsUnknown() {
        // Arrange
        var registry = CreateRegistryWithInstruments(("AAA", "AAA", "TSX"));
        registry.SetPriorityCompanies(new[] { "UNKNOWN1", "UNKNOWN2" });

        // Act
        bool found = registry.TryDequeueNextPriorityInstrumentKey(out var key);

        // Assert
        found.Should().BeFalse();
        key.Should().BeNull();
    }

    [Fact]
    public void GetPriorityCompanySymbols_ReturnsSnapshotWithoutConsuming() {
        // Arrange
        var registry = CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("BBB", "BBB", "TSX"));
        registry.SetPriorityCompanies(new[] { "AAA", "BBB" });

        // Act
        var snapshot1 = registry.GetPriorityCompanySymbols();
        var snapshot2 = registry.GetPriorityCompanySymbols();

        // Assert
        snapshot1.Should().Equal("AAA", "BBB");
        snapshot2.Should().Equal("AAA", "BBB");
    }

    [Fact]
    public void ClearPriorityCompanies_EmptiesQueue() {
        // Arrange
        var registry = CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("BBB", "BBB", "TSX"));
        registry.SetPriorityCompanies(new[] { "AAA", "BBB" });

        // Act
        registry.ClearPriorityCompanies();

        // Assert
        registry.GetPriorityCompanySymbols().Should().BeEmpty();
    }

    [Fact]
    public void SetPriorityCompanies_ReplacesOldQueue() {
        // Arrange
        var registry = CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("BBB", "BBB", "TSX"),
            ("CCC", "CCC", "TSX"));
        registry.SetPriorityCompanies(new[] { "AAA", "BBB" });

        // Act
        registry.SetPriorityCompanies(new[] { "CCC" });

        // Assert
        registry.GetPriorityCompanySymbols().Should().Equal("CCC");
    }

    [Fact]
    public void TryDequeueNextPriorityInstrumentKey_ReturnsFirstInstrumentForCompany() {
        // Arrange: Company "AAA" has two instruments
        var registry = new Registry();
        var instruments = new List<InstrumentDto> {
            new(1, "TSX", "AAA", "AAA Inc.", "AAA", "Common Shares", DateTimeOffset.UtcNow, null),
            new(2, "TSX", "AAA", "AAA Inc.", "AAA.PR.A", "Preferred A", DateTimeOffset.UtcNow, null),
        };
        registry.InitializeDirectory(instruments);
        registry.SetPriorityCompanies(new[] { "AAA" });

        // Act
        bool found = registry.TryDequeueNextPriorityInstrumentKey(out var key);

        // Assert
        found.Should().BeTrue();
        key!.CompanySymbol.Should().Be("AAA");
        key.InstrumentSymbol.Should().Be("AAA"); // First instrument (sorted)
    }
}
