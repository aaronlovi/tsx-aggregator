using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using tsx_aggregator.models;
using tsx_aggregator.Raw;
using tsx_aggregator.shared;

namespace tsx_aggregator.tests;

public class RegistryLoaderTests {
    private static InstrumentDto Instrument(long id, string symbol) =>
        new(id, "TSX", symbol, symbol + " Inc.", symbol, symbol + " Common", DateTimeOffset.UtcNow, null);

    private static RegistryLoader CreateLoader(Registry registry, IReadOnlyList<InstrumentDto> dbInstruments, bool rawCollectorEnabled, bool dbSucceeds = true) {
        var dbm = new Mock<IDbmService>();
        _ = dbm.Setup(d => d.GetInstrumentList(It.IsAny<CancellationToken>()))
            .ReturnsAsync((dbSucceeds ? Result.SUCCESS : Result.SetFailure("db down"), dbInstruments));
        IOptions<HostedServicesOptions> options = Options.Create(new HostedServicesOptions { RunRawCollector = rawCollectorEnabled });
        return new RegistryLoader(registry, dbm.Object, options, NullLogger<RegistryLoader>.Instance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartAsync_LoadsInstrumentsAndSignalsRegistry_WhetherOrNotRawCollectorRuns(bool rawCollectorEnabled) {
        var registry = new Registry();
        RegistryLoader loader = CreateLoader(registry, [Instrument(1, "AAA"), Instrument(2, "BBB")], rawCollectorEnabled);

        await loader.StartAsync(CancellationToken.None);

        _ = registry.DirectoryInitialized.Task.IsCompleted.Should().BeTrue();
        _ = registry.NumInstruments.Should().Be(2);
    }

    [Fact]
    public async Task StartAsync_EmptyDatabaseWithRawCollector_LeavesSignalToRawCollector() {
        var registry = new Registry();
        RegistryLoader loader = CreateLoader(registry, [], rawCollectorEnabled: true);

        await loader.StartAsync(CancellationToken.None);

        _ = registry.DirectoryInitialized.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_EmptyDatabaseWithoutRawCollector_SignalsEmptyRegistry() {
        var registry = new Registry();
        RegistryLoader loader = CreateLoader(registry, [], rawCollectorEnabled: false);

        await loader.StartAsync(CancellationToken.None);

        _ = registry.DirectoryInitialized.Task.IsCompleted.Should().BeTrue();
        _ = registry.NumInstruments.Should().Be(0);
    }

    [Fact]
    public async Task StartAsync_DatabaseFailure_Throws() {
        RegistryLoader loader = CreateLoader(new Registry(), [], rawCollectorEnabled: true, dbSucceeds: false);

        Func<Task> act = () => loader.StartAsync(CancellationToken.None);

        _ = await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void InitializeDirectory_CalledTwice_ReplacesRatherThanDuplicates() {
        var registry = new Registry();

        registry.InitializeDirectory([Instrument(1, "AAA"), Instrument(2, "BBB")]);
        registry.InitializeDirectory([Instrument(1, "AAA"), Instrument(3, "CCC")]);

        _ = registry.NumInstruments.Should().Be(2);
    }
}
