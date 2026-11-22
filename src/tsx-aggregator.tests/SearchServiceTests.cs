using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using tsx_aggregator.models;
using tsx_aggregator.Raw;
using tsx_aggregator.Services;
using tsx_aggregator.shared;
using Xunit;

namespace tsx_aggregator.tests;

/// <summary>
/// Quick search (review finding R40 part 2, and R32 on result ranking): the SearchService is started,
/// the Registry's directory is loaded, and search requests are posted the way StockDataSvc posts them.
/// Covers matching on instrument symbols and company names (with and without an "exchange:" prefix),
/// case handling, no matches, the 5-result limit, the exact symbol match coming first, what a registry
/// change does before and after the index is rebuilt, and readiness before the directory is loaded.
/// </summary>
public sealed class SearchServiceTests : IAsyncLifetime {
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private const string Tsx = Constants.TsxExchange;

    private readonly Registry _registry = new();
    private readonly SearchService _service;
    private readonly CancellationTokenSource _stop = new();
    private long _instrumentId;
    private long _reqId;

    public SearchServiceTests() {
        var services = new ServiceCollection();
        _ = services.AddSingleton<ILogger<SearchService>>(new NullLogger<SearchService>());
        _ = services.AddSingleton(_registry);
        _service = new SearchService(services.BuildServiceProvider());
    }

    public async Task InitializeAsync() => await _service.StartAsync(_stop.Token);

    public async Task DisposeAsync() {
        await _stop.CancelAsync();
        await _service.StopAsync(CancellationToken.None);
        _service.Dispose();
        _stop.Dispose();
    }

    private InstrumentDto Instrument(string companySymbol, string instrumentSymbol, string companyName, string exchange = Tsx) =>
        new(++_instrumentId, exchange, companySymbol, companyName, instrumentSymbol, companyName,
            new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero), null);

    private InstrumentDto[] BigCanadianNames() => [
        Instrument("RY", "RY", "Royal Bank of Canada"),
        Instrument("TD", "TD", "Toronto-Dominion Bank"),
        Instrument("ENB", "ENB", "Enbridge Inc."),
        Instrument("SU", "SU", "Suncor Energy Inc."),
        Instrument("CNQ", "CNQ", "Canadian Natural Resources Limited"),
        Instrument("CNR", "CNR", "Canadian National Railway Company"),
        Instrument("SHOP", "SHOP", "Shopify Inc."),
    ];

    private async Task LoadDirectory(params InstrumentDto[] instruments) {
        _registry.InitializeDirectory(instruments);
        await _service.SearchServiceReady.Task.WaitAsync(Wait);
    }

    private async Task<List<GetStockSearchResultsReplyItem>> Search(string term) {
        using var req = new SearchServiceQuickSearchRequestInput(++_reqId, term, new CancellationTokenSource());
        _ = _service.PostRequest(req).Should().BeTrue();
        object? response = await req.Completed.Task.WaitAsync(Wait);
        return response.Should().BeOfType<List<GetStockSearchResultsReplyItem>>().Subject;
    }

    private static IEnumerable<string> Symbols(IEnumerable<GetStockSearchResultsReplyItem> results) =>
        results.Select(r => r.InstrumentSymbol);

    [Fact]
    public async Task CancelledRequest_IsCompletedAsCancelled() {
        // The loop used to return without completing a request whose caller had gone, leaving Completed unset.
        await LoadDirectory(Instrument("RY", "RY", "Royal Bank of Canada"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var req = new SearchServiceQuickSearchRequestInput(++_reqId, "ry", cts);
        _ = _service.PostRequest(req).Should().BeTrue();

        Func<Task> wait = () => req.Completed.Task.WaitAsync(Wait);

        _ = await wait.Should().ThrowAsync<TaskCanceledException>();
    }

    // ---- Prefix matching on symbols and names --------------------------------------------------

    [Fact]
    public async Task SymbolPrefix_FindsTheInstrument_WithItsNameSymbolAndExchange() {
        await LoadDirectory(BigCanadianNames());

        // "RY" matches only the symbol; no company name starts with "ry".
        List<GetStockSearchResultsReplyItem> results = await Search("RY");

        _ = results.Should().ContainSingle();
        _ = results[0].InstrumentSymbol.Should().Be("RY");
        _ = results[0].CompanyName.Should().Be("Royal Bank of Canada");
        _ = results[0].Exchange.Should().Be(Tsx);
    }

    [Fact]
    public async Task NamePrefix_FindsTheCompany_IncludingSpacesInTheTerm() {
        await LoadDirectory(BigCanadianNames());

        _ = Symbols(await Search("Royal Bank")).Should().Equal("RY");
        _ = Symbols(await Search("Canadian Na")).Should().BeEquivalentTo(["CNQ", "CNR"]);
        _ = Symbols(await Search("Canadian Nat")).Should().BeEquivalentTo(["CNQ", "CNR"]);
        _ = Symbols(await Search("Canadian Natural")).Should().Equal("CNQ");
    }

    [Fact]
    public async Task NameIsMatchedFromItsStartOnly_NotOnLaterWords() {
        await LoadDirectory(BigCanadianNames());

        // "Bank" appears in "Royal Bank of Canada" and "Toronto-Dominion Bank", but neither name starts with it.
        _ = (await Search("Bank")).Should().BeEmpty();
    }

    [Fact]
    public async Task SymbolAndNameBothMatching_ReturnsTheInstrumentOnce() {
        await LoadDirectory(BigCanadianNames());

        // "s" is the start of the symbols SU and SHOP and of the names Suncor and Shopify.
        _ = Symbols(await Search("s")).Should().BeEquivalentTo(["SU", "SHOP"]);
    }

    [Fact]
    public async Task EachInstrumentOfACompany_IsItsOwnResult() {
        await LoadDirectory(
            Instrument("ENB", "ENB", "Enbridge Inc."),
            Instrument("ENB", "ENB.PF.A", "Enbridge Inc."),
            Instrument("ENB", "ENB.PR.D", "Enbridge Inc."),
            Instrument("SU", "SU", "Suncor Energy Inc."));

        _ = Symbols(await Search("enb")).Should().BeEquivalentTo(["ENB", "ENB.PF.A", "ENB.PR.D"]);
        _ = Symbols(await Search("enb.p")).Should().BeEquivalentTo(["ENB.PF.A", "ENB.PR.D"]);
    }

    // ---- Case handling ---------------------------------------------------------------------

    [Theory]
    [InlineData("ry")]
    [InlineData("Ry")]
    [InlineData("rY")]
    [InlineData("ROYAL BANK")]
    [InlineData("royal bank")]
    [InlineData("rOyAl")]
    public async Task Search_IgnoresCase(string term) {
        await LoadDirectory(BigCanadianNames());

        _ = Symbols(await Search(term)).Should().Equal("RY");
    }

    [Fact]
    public async Task Results_KeepTheRegistrysOriginalCasing() {
        await LoadDirectory(BigCanadianNames());

        GetStockSearchResultsReplyItem item = (await Search("shopify")).Should().ContainSingle().Subject;

        _ = item.InstrumentSymbol.Should().Be("SHOP");
        _ = item.CompanyName.Should().Be("Shopify Inc.");
    }

    // ---- Exchange-qualified search -----------------------------------------------------------

    [Fact]
    public async Task ExchangePrefix_MatchesSymbolsAndNamesOnThatExchangeOnly() {
        await LoadDirectory(
            Instrument("RY", "RY", "Royal Bank of Canada"),
            Instrument("SU", "SU", "Suncor Energy Inc."),
            Instrument("SLS", "SLS", "Solaris Resources Inc.", exchange: "TSXV"),
            Instrument("SGML", "SGML", "Sigma Lithium Corporation", exchange: "TSXV"));

        _ = Symbols(await Search("TSX:RY")).Should().Equal("RY");
        _ = Symbols(await Search("tsx:royal")).Should().Equal("RY");
        _ = Symbols(await Search("tsx:s")).Should().Equal("SU");
        _ = Symbols(await Search("TSXV:S")).Should().BeEquivalentTo(["SLS", "SGML"]);
        _ = (await Search("TSXV:RY")).Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutAnExchangePrefix_EveryExchangeIsSearched() {
        await LoadDirectory(
            Instrument("SU", "SU", "Suncor Energy Inc."),
            Instrument("SLS", "SLS", "Solaris Resources Inc.", exchange: "TSXV"));

        _ = Symbols(await Search("s")).Should().BeEquivalentTo(["SU", "SLS"]);
    }

    // ---- No matches ---------------------------------------------------------------------------

    [Theory]
    [InlineData("zzz")]
    [InlineData("ryx")]            // a known prefix followed by a character that continues nothing
    [InlineData("royal bank of canadaX")]
    [InlineData("nyse:ry")]        // an exchange that is not in the registry
    public async Task NoMatch_ReturnsAnEmptyList(string term) {
        await LoadDirectory(BigCanadianNames());

        _ = (await Search(term)).Should().BeEmpty();
    }

    // ---- At most 5 results, exact symbol first (R32) ------------------------------------------

    private InstrumentDto[] CompaniesStartingWithBa() => [
        Instrument("ABX", "ABX", "Barrick Mining Corporation"),
        Instrument("BDGI", "BDGI", "Badger Infrastructure Solutions Ltd."),
        Instrument("BHC", "BHC", "Bausch Health Companies Inc."),
        Instrument("BLDP", "BLDP", "Ballard Power Systems Inc."),
        Instrument("BMO", "BMO", "Bank of Montreal"),
        Instrument("BNS", "BNS", "Bank of Nova Scotia"),
        Instrument("BTE", "BTE", "Baytex Energy Corp."),
    ];

    [Fact]
    public async Task MoreThanFiveMatches_ReturnsFiveDistinctMatches() {
        InstrumentDto[] matches = CompaniesStartingWithBa();
        await LoadDirectory([.. matches, Instrument("RY", "RY", "Royal Bank of Canada")]);

        List<GetStockSearchResultsReplyItem> results = await Search("ba");

        _ = results.Should().HaveCount(5);
        _ = Symbols(results).Should().OnlyHaveUniqueItems();
        _ = Symbols(results).Should().BeSubsetOf(matches.Select(m => m.InstrumentSymbol));
    }

    [Fact]
    public async Task ExactlyFiveMatches_ReturnsAllFive() {
        InstrumentDto[] matches = CompaniesStartingWithBa().Take(5).ToArray();
        await LoadDirectory(matches);

        _ = Symbols(await Search("ba")).Should().BeEquivalentTo(matches.Select(m => m.InstrumentSymbol));
    }

    [Fact]
    public async Task ExactSymbolMatch_ComesFirst_EvenWithMoreThanFiveLongerMatches() {
        // R32: the 5 results are not ranked. The order is deterministic, though: the instrument whose
        // symbol (or name) is exactly the search term sits on the node the search ends at, and that
        // node's own entries are collected before anything below it. Brookfield Corporation (BN) has
        // many preferred shares, all of which also start with "bn", plus BNS starts with "bn".
        await LoadDirectory(
            Instrument("BN", "BN", "Brookfield Corporation"),
            Instrument("BN", "BN.PF.A", "Brookfield Corporation"),
            Instrument("BN", "BN.PF.B", "Brookfield Corporation"),
            Instrument("BN", "BN.PF.C", "Brookfield Corporation"),
            Instrument("BN", "BN.PF.D", "Brookfield Corporation"),
            Instrument("BN", "BN.PF.E", "Brookfield Corporation"),
            Instrument("BN", "BN.PR.B", "Brookfield Corporation"),
            Instrument("BNS", "BNS", "Bank of Nova Scotia"));

        List<GetStockSearchResultsReplyItem> results = await Search("BN");

        _ = results.Should().HaveCount(5);
        _ = results[0].InstrumentSymbol.Should().Be("BN");
        _ = results[0].CompanyName.Should().Be("Brookfield Corporation");
    }

    [Fact]
    public async Task ExactSymbolMatch_ComesFirst_WhenTheTermIsAlsoTheStartOfOtherNames() {
        // "T" is Telus's symbol and the first letter of six other names and symbols.
        await LoadDirectory(
            Instrument("TA", "TA", "TransAlta Corporation"),
            Instrument("TD", "TD", "Toronto-Dominion Bank"),
            Instrument("TECK.B", "TECK.B", "Teck Resources Limited"),
            Instrument("TFII", "TFII", "TFI International Inc."),
            Instrument("TOU", "TOU", "Tourmaline Oil Corp."),
            Instrument("TRP", "TRP", "TC Energy Corporation"),
            Instrument("T", "T", "Telus Corporation"));

        List<GetStockSearchResultsReplyItem> results = await Search("t");

        _ = results.Should().HaveCount(5);
        _ = results[0].InstrumentSymbol.Should().Be("T");
    }

    // ---- Registry changes and the index rebuild ----------------------------------------------

    private async Task RebuildIndex() {
        // The service rebuilds every five minutes by posting itself this input; post it directly instead.
        // The input queue is processed in order, so any search posted after it sees the new index.
        _ = _service.PostRequest(new SearchServiceTimeoutInput(reqId: 0, cancellationTokenSource: null, curTimeUtc: DateTime.UtcNow))
            .Should().BeTrue();
        _ = await Search("anything, to wait for the rebuild");
    }

    [Fact]
    public async Task AddedInstrument_IsNotFoundUntilTheIndexIsRebuilt() {
        await LoadDirectory(BigCanadianNames());
        _registry.AddInstrument(Instrument("CSU", "CSU", "Constellation Software Inc."));

        _ = (await Search("csu")).Should().BeEmpty();
        _ = (await Search("constellation")).Should().BeEmpty();

        await RebuildIndex();

        _ = Symbols(await Search("csu")).Should().Equal("CSU");
        _ = Symbols(await Search("constellation")).Should().Equal("CSU");
    }

    [Fact]
    public async Task RemovedInstrument_IsLeftOutRightAway_AndTheRestStillMatch() {
        InstrumentDto[] directory = BigCanadianNames();
        await LoadDirectory(directory);
        InstrumentDto cnq = directory.Single(i => i.InstrumentSymbol == "CNQ");

        _registry.RemoveInstrument(cnq);

        // Still in the old index, but every hit is checked against the registry before it is returned.
        _ = Symbols(await Search("cn")).Should().Equal("CNR");

        await RebuildIndex();

        _ = Symbols(await Search("cn")).Should().Equal("CNR");
        _ = (await Search("canadian natural")).Should().BeEmpty();
    }

    [Fact]
    public async Task ReplacedDirectory_IsSearchedAfterTheRebuild() {
        await LoadDirectory(BigCanadianNames());

        _registry.InitializeDirectory([
            Instrument("RY", "RY", "Royal Bank of Canada"),
            Instrument("WCN", "WCN", "Waste Connections Inc."),
        ]);
        await RebuildIndex();

        _ = Symbols(await Search("w")).Should().Equal("WCN");
        _ = (await Search("td")).Should().BeEmpty();
        _ = Symbols(await Search("ry")).Should().Equal("RY");
    }

    [Fact]
    public async Task RenamedCompany_IsFoundByItsNewNameAfterTheRebuild() {
        await LoadDirectory(Instrument("ABX", "ABX", "Barrick Gold Corporation"));

        InstrumentDto oldName = _registry.GetInstruments().Single();
        _registry.RemoveInstrument(oldName);
        _registry.AddInstrument(Instrument("ABX", "ABX", "Barrick Mining Corporation"));

        // Before the rebuild the old name still finds the instrument, which reports its current name.
        _ = (await Search("barrick g")).Should().ContainSingle().Which.CompanyName.Should().Be("Barrick Mining Corporation");

        await RebuildIndex();

        _ = (await Search("barrick g")).Should().BeEmpty();
        _ = Symbols(await Search("barrick m")).Should().Equal("ABX");
    }

    // ---- Before the directory is loaded ------------------------------------------------------

    [Fact]
    public async Task NotReady_UntilTheRegistryDirectoryIsLoaded() {
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        _ = _service.SearchServiceReady.Task.IsCompleted.Should().BeFalse();

        _registry.InitializeDirectory(BigCanadianNames());

        await _service.SearchServiceReady.Task.WaitAsync(Wait);
        _ = Symbols(await Search("ry")).Should().Equal("RY");
    }

    [Fact]
    public async Task RequestPostedBeforeTheDirectoryIsLoaded_IsNotAnsweredUntilThen() {
        using var early = new SearchServiceQuickSearchRequestInput(++_reqId, "ry", new CancellationTokenSource());
        _ = _service.PostRequest(early).Should().BeTrue();

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        _ = early.Completed.Task.IsCompleted.Should().BeFalse();

        _registry.InitializeDirectory(BigCanadianNames());

        _ = await early.Completed.Task.WaitAsync(Wait);
    }
}
