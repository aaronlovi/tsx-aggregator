using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using tsx_aggregator.models;
using tsx_aggregator.Raw;
using tsx_aggregator.shared;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// The uniq_instruments constraint spans (exchange, company_symbol, instrument_symbol) and
/// ignores obsoleted_date, so a soft-deleted row keeps owning its natural key. An instrument
/// that gets re-listed therefore has to be updated in place; inserting it again collides and
/// the instrument can never come back.
/// </summary>
public class RawCollectorRegistryDiffTests {
    private const string Exchange = Constants.TsxExchange;

    private readonly Mock<IDbmService> _dbm = new(MockBehavior.Strict);

    private RawCollector BuildRawCollector(Registry registry) {
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<RawCollector>>(new NullLogger<RawCollector>());
        _ = services.AddSingleton<ILogger<RawCollectorFsm>>(new NullLogger<RawCollectorFsm>());
        _ = services.AddSingleton(_dbm.Object);
        _ = services.AddSingleton(registry);
        _ = services.AddSingleton(Mock.Of<IHttpClientFactory>());
        return new RawCollector(services.BuildServiceProvider());
    }

    private static Dictionary<string, InstrumentDtoByInstrumentNameMap> DirectoryOf(
        params (string CompanySymbol, string InstrumentSymbol)[] instruments) {
        var directory = new Dictionary<string, InstrumentDtoByInstrumentNameMap>();
        foreach (var (companySymbol, instrumentSymbol) in instruments) {
            if (!directory.TryGetValue(companySymbol, out InstrumentDtoByInstrumentNameMap? map))
                directory[companySymbol] = map = new InstrumentDtoByInstrumentNameMap();
            map[instrumentSymbol] = new InstrumentDto(
                0, Exchange, companySymbol, companySymbol + " Inc.", instrumentSymbol, instrumentSymbol + " Common",
                DateTimeOffset.UtcNow, null);
        }
        return directory;
    }

    private void ExpectObsoletedIds(params (string CompanySymbol, string InstrumentSymbol, long InstrumentId)[] rows) {
        var map = new Dictionary<InstrumentKey, long>();
        foreach (var (companySymbol, instrumentSymbol, instrumentId) in rows)
            map[new InstrumentKey(companySymbol, instrumentSymbol, Exchange)] = instrumentId;

        _dbm.Setup(x => x.GetObsoletedInstrumentIds(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SUCCESS, (IReadOnlyDictionary<InstrumentKey, long>)map));
    }

    [Fact]
    public async Task UpdateRegistry_WhenInstrumentWasPreviouslyObsoleted_RevivesExistingRow() {
        // Arrange: BMO is listed again, but the registry only holds active instruments so it
        // looks new. Its soft-deleted row still exists under instrument_id 89.
        var registry = new Registry();
        ExpectObsoletedIds(("BMO", "BMO", 89L));
        RawCollector collector = BuildRawCollector(registry);

        // Act
        UpdateRegistryResults? results = await collector.UpdateRegistry(DirectoryOf(("BMO", "BMO")), CancellationToken.None);

        // Assert
        _ = results.Should().NotBeNull();
        _ = results!.NewInstruments.Should().BeEmpty("a row already owns that natural key");
        _ = results.RevivedInstruments.Should().ContainSingle();
        _ = results.RevivedInstruments[0].InstrumentSymbol.Should().Be("BMO");
        _ = results.RevivedInstruments[0].InstrumentId.Should().Be(89L, "the existing row keeps its reports");
        _ = results.ObsoletedInstruments.Should().BeEmpty();

        // No id may be burned on a revive; doing so is what orphaned the instrument events.
        _dbm.Verify(x => x.GetNextId64(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRegistry_WhenInstrumentIsGenuinelyNew_AllocatesFreshId() {
        // Arrange
        var registry = new Registry();
        ExpectObsoletedIds();
        _ = _dbm.Setup(x => x.GetNextId64(It.IsAny<CancellationToken>())).ReturnsAsync(4242UL);
        RawCollector collector = BuildRawCollector(registry);

        // Act
        UpdateRegistryResults? results = await collector.UpdateRegistry(DirectoryOf(("NEW", "NEW")), CancellationToken.None);

        // Assert
        _ = results.Should().NotBeNull();
        _ = results!.RevivedInstruments.Should().BeEmpty();
        _ = results.NewInstruments.Should().ContainSingle();
        _ = results.NewInstruments[0].InstrumentId.Should().Be(4242L);
    }

    [Fact]
    public async Task UpdateRegistry_SeparatesRevivedFromNewInTheSameFetch() {
        // Arrange
        var registry = new Registry();
        ExpectObsoletedIds(("BMO", "BMO", 89L));
        _ = _dbm.Setup(x => x.GetNextId64(It.IsAny<CancellationToken>())).ReturnsAsync(4242UL);
        RawCollector collector = BuildRawCollector(registry);

        // Act
        UpdateRegistryResults? results = await collector.UpdateRegistry(
            DirectoryOf(("BMO", "BMO"), ("NEW", "NEW")), CancellationToken.None);

        // Assert
        _ = results.Should().NotBeNull();
        _ = results!.RevivedInstruments.Should().ContainSingle().Which.InstrumentId.Should().Be(89L);
        _ = results.NewInstruments.Should().ContainSingle().Which.InstrumentId.Should().Be(4242L);
        _dbm.Verify(x => x.GetNextId64(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRegistry_WhenObsoletedIdLookupFails_AbortsInsteadOfInserting() {
        // Arrange: without the id map we cannot tell a revive from an insert, and guessing
        // "insert" is what silently dropped the instrument before.
        var registry = new Registry();
        _dbm.Setup(x => x.GetObsoletedInstrumentIds(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SetFailure("boom"), (IReadOnlyDictionary<InstrumentKey, long>)new Dictionary<InstrumentKey, long>()));
        RawCollector collector = BuildRawCollector(registry);

        // Act
        UpdateRegistryResults? results = await collector.UpdateRegistry(DirectoryOf(("BMO", "BMO")), CancellationToken.None);

        // Assert
        _ = results.Should().BeNull();
        _dbm.Verify(x => x.GetNextId64(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRegistry_ObsoletesInstrumentsMissingFromTheDirectory() {
        // Arrange
        var registry = TestDataFactory.CreateRegistryWithInstruments(
            ("AAA", "AAA", Exchange),
            ("BBB", "BBB", Exchange));
        ExpectObsoletedIds();
        RawCollector collector = BuildRawCollector(registry);

        // Act
        UpdateRegistryResults? results = await collector.UpdateRegistry(DirectoryOf(("AAA", "AAA")), CancellationToken.None);

        // Assert
        _ = results.Should().NotBeNull();
        _ = results!.ObsoletedInstruments.Should().ContainSingle().Which.InstrumentSymbol.Should().Be("BBB");
        _ = results.NewInstruments.Should().BeEmpty();
        _ = results.RevivedInstruments.Should().BeEmpty();
    }
}
