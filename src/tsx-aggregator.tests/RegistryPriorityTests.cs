using System;
using System.Collections.Generic;
using FluentAssertions;
using tsx_aggregator.models;
using tsx_aggregator.Raw;


namespace tsx_aggregator.tests;

public class RegistryPriorityTests {
    [Fact]
    public void SetPriorityCompanies_PopulatesQueue() {
        // Arrange
        var registry = TestDataFactory.CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("BBB", "BBB", "TSX"),
            ("CCC", "CCC", "TSX"));

        // Act
        int validCount = registry.SetPriorityCompanies(new[] { "CCC", "AAA" });

        // Assert
        _ = validCount.Should().Be(2);
        _ = registry.GetPriorityCompanySymbols().Should().Equal("CCC", "AAA");
    }

    [Fact]
    public void SetPriorityCompanies_DeduplicatesSymbols() {
        // Arrange
        var registry = TestDataFactory.CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("BBB", "BBB", "TSX"));

        // Act
        int validCount = registry.SetPriorityCompanies(new[] { "AAA", "BBB", "AAA", "BBB" });

        // Assert
        _ = validCount.Should().Be(2);
        _ = registry.GetPriorityCompanySymbols().Should().Equal("AAA", "BBB");
    }

    [Fact]
    public void TryDequeueNextPriorityInstrumentKey_ReturnsCorrectKey() {
        // Arrange
        var registry = TestDataFactory.CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("BBB", "BBB", "TSX"),
            ("CCC", "CCC", "TSX"));
        _ = registry.SetPriorityCompanies(new[] { "BBB" });

        // Act
        bool found = registry.TryDequeueNextPriorityInstrumentKey(out var key);

        // Assert
        _ = found.Should().BeTrue();
        _ = key.Should().NotBeNull();
        _ = key!.CompanySymbol.Should().Be("BBB");
        _ = key.InstrumentSymbol.Should().Be("BBB");
        _ = key.Exchange.Should().Be("TSX");
    }

    [Fact]
    public void TryDequeueNextPriorityInstrumentKey_SkipsUnknownSymbols() {
        // Arrange
        var registry = TestDataFactory.CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("CCC", "CCC", "TSX"));
        _ = registry.SetPriorityCompanies(new[] { "UNKNOWN", "CCC" });

        // Act
        bool found = registry.TryDequeueNextPriorityInstrumentKey(out var key);

        // Assert
        _ = found.Should().BeTrue();
        _ = key!.CompanySymbol.Should().Be("CCC");
    }

    [Fact]
    public void TryDequeueNextPriorityInstrumentKey_ReturnsFalseWhenEmpty() {
        // Arrange
        var registry = TestDataFactory.CreateRegistryWithInstruments(("AAA", "AAA", "TSX"));

        // Act
        bool found = registry.TryDequeueNextPriorityInstrumentKey(out var key);

        // Assert
        _ = found.Should().BeFalse();
        _ = key.Should().BeNull();
    }

    [Fact]
    public void TryDequeueNextPriorityInstrumentKey_ReturnsFalseWhenAllSymbolsUnknown() {
        // Arrange
        var registry = TestDataFactory.CreateRegistryWithInstruments(("AAA", "AAA", "TSX"));
        _ = registry.SetPriorityCompanies(new[] { "UNKNOWN1", "UNKNOWN2" });

        // Act
        bool found = registry.TryDequeueNextPriorityInstrumentKey(out var key);

        // Assert
        _ = found.Should().BeFalse();
        _ = key.Should().BeNull();
    }

    [Fact]
    public void GetPriorityCompanySymbols_ReturnsSnapshotWithoutConsuming() {
        // Arrange
        var registry = TestDataFactory.CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("BBB", "BBB", "TSX"));
        _ = registry.SetPriorityCompanies(new[] { "AAA", "BBB" });

        // Act
        var snapshot1 = registry.GetPriorityCompanySymbols();
        var snapshot2 = registry.GetPriorityCompanySymbols();

        // Assert
        _ = snapshot1.Should().Equal("AAA", "BBB");
        _ = snapshot2.Should().Equal("AAA", "BBB");
    }

    [Fact]
    public void ClearPriorityCompanies_EmptiesQueue() {
        // Arrange
        var registry = TestDataFactory.CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("BBB", "BBB", "TSX"));
        _ = registry.SetPriorityCompanies(new[] { "AAA", "BBB" });

        // Act
        registry.ClearPriorityCompanies();

        // Assert
        _ = registry.GetPriorityCompanySymbols().Should().BeEmpty();
    }

    [Fact]
    public void SetPriorityCompanies_ReplacesOldQueue() {
        // Arrange
        var registry = TestDataFactory.CreateRegistryWithInstruments(
            ("AAA", "AAA", "TSX"),
            ("BBB", "BBB", "TSX"),
            ("CCC", "CCC", "TSX"));
        _ = registry.SetPriorityCompanies(new[] { "AAA", "BBB" });

        // Act
        _ = registry.SetPriorityCompanies(new[] { "CCC" });

        // Assert
        _ = registry.GetPriorityCompanySymbols().Should().Equal("CCC");
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
        _ = registry.SetPriorityCompanies(new[] { "AAA" });

        // Act
        bool found = registry.TryDequeueNextPriorityInstrumentKey(out var key);

        // Assert
        _ = found.Should().BeTrue();
        _ = key!.CompanySymbol.Should().Be("AAA");
        _ = key.InstrumentSymbol.Should().Be("AAA"); // First instrument (sorted)
    }
}
