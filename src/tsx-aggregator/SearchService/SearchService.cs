using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tsx_aggregator.models;
using tsx_aggregator.Raw;
using tsx_aggregator.shared;

namespace tsx_aggregator.Services;

public class SearchService : BackgroundService, ISearchService {
    private readonly ILogger _logger;
    private readonly Registry _registry;
    private readonly IServiceProvider _svp;
    private readonly Channel<SearchServiceInputBase> _inputChannel;

    private TrieNode<InstrumentKey> _searchByCompanyRoot;
    private TrieNode<InstrumentKey> _searchByExchangeRoot;

    public SearchService(IServiceProvider svp) {
        _logger = svp.GetRequiredService<ILogger<SearchService>>();
        _registry = svp.GetRequiredService<Registry>();
        _svp = svp;
        _inputChannel = Channel.CreateUnbounded<SearchServiceInputBase>();
        SearchServiceReady = new();
        _searchByCompanyRoot = new();
        _searchByExchangeRoot = new();

        _logger.LogInformation("SearchService - Created");
    }

    public TaskCompletionSource SearchServiceReady { get; }

    public bool PostRequest(SearchServiceInputBase inp) {
        return _inputChannel.Writer.TryWrite(inp);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _ = StartHeartbeat(_svp, stoppingToken); // Fire and forget

        _logger.LogInformation("SearchService - Entering main loop");

        // Wait for the registry to contain the list of stocks
        await _registry.DirectoryInitialized.Task;

        _logger.LogInformation("SearchService - Directory initialized");

        _ = PostRequest(new SearchServiceTimeoutInput(reqId: 0, cancellationTokenSource: null, curTimeUtc: DateTime.UtcNow));

        while (!stoppingToken.IsCancellationRequested) {
            await foreach (SearchServiceInputBase input in _inputChannel.Reader.ReadAllAsync(stoppingToken)) {
                _logger.LogInformation("SearchService - Got a message {Input}", input);

                using var reqIdContext = _logger.BeginScope(new Dictionary<string, long> { [LogUtils.ReqIdContext] = input.ReqId });
                using var thisRequestCts = Utilities.CreateLinkedTokenSource(input.CancellationTokenSource, stoppingToken);

                switch (input) {
                    case SearchServiceTimeoutInput timeoutInput: {
                        HandleTimeout(timeoutInput, thisRequestCts.Token);
                        break;
                    }
                    case SearchServiceQuickSearchRequestInput searchInput: {
                        HandleQuickSearch(searchInput, thisRequestCts.Token);
                        break;
                    }
                    default: {
                        _logger.LogError("SearchService main loop - Invalid request type received, dropping input");
                        break;
                    }
                }
            }
        }

        _logger.LogInformation("SearchService - Exiting main loop");
    }

    private void HandleTimeout(SearchServiceTimeoutInput ti, CancellationToken ct) {
        _logger.LogInformation("HandleTimeout {CurTime}", ti.CurTimeUtc);

        try {
            InitializeSearchTrie();

            // Mark the Search service as ready to accept search requests
            SearchServiceReady.TrySetResult();

        } catch (OperationCanceledException) {
            _logger.LogInformation("HandleTimeout - Operation cancelled");
        } catch (Exception ex) {
            _logger.LogError(ex, "HandleTimeout - general fault");
        } finally {
            // Setup the next timeout task
            SetupTimeoutTask(ct);
        }
    }

    private void InitializeSearchTrie() {
        _searchByCompanyRoot = new();
        _searchByExchangeRoot = new();
        IReadOnlyCollection<InstrumentDto> companies = _registry.GetInstruments();
        BuildTrie(companies);
    }

    private void SetupTimeoutTask(CancellationToken ct) {
        _ = Task.Delay(Constants.FiveMinutes, ct).ContinueWith(t => {
            if (t.IsCanceled) {
                _logger.LogInformation("SetupTimeoutTask - Timeout task cancelled");
                return;
            } else if (t.IsFaulted) {
                Exception? outerEx = t.Exception;
                Exception? innerEx = outerEx?.InnerException;
                _logger.LogError(innerEx, "SetupTimeoutTask - unexpected faulted state. Outer exception: {OuterMessage}. Inner Exception: {Message}, StackTrace: {StackTrace}",
                    outerEx?.Message, innerEx?.Message, innerEx?.StackTrace);
                return;
            } else {
                _logger.LogInformation("SetupTimeoutTask - Timeout reached, posting timeout message");
                _inputChannel.Writer.TryWrite(new SearchServiceTimeoutInput(reqId: 0, cancellationTokenSource: null, curTimeUtc: DateTime.UtcNow));
            }
        }, ct);
    }

    /// <summary>
    /// Method to build the Trie from the company registry
    /// </summary>
    private void BuildTrie(IReadOnlyCollection<InstrumentDto> companies) {
        foreach (var company in companies) {
            string symbol = company.InstrumentSymbol.ToLower();
            string name = company.CompanyName.ToLower();
            string exchange = company.Exchange.ToLower();

            InsertIntoTrie(_searchByCompanyRoot, symbol, company.CompanySymbol, company.InstrumentSymbol, company.Exchange);
            InsertIntoTrie(_searchByCompanyRoot, name, company.CompanySymbol, company.InstrumentSymbol, company.Exchange);

            InsertIntoTrie(_searchByExchangeRoot, exchange + ":" + symbol, company.CompanySymbol, company.InstrumentSymbol, company.Exchange);
            InsertIntoTrie(_searchByExchangeRoot, exchange + ":" + name, company.CompanySymbol, company.InstrumentSymbol, company.Exchange);
        }
    }

    /// <summary>
    /// Method to insert a word into the Trie
    /// </summary>
    private static void InsertIntoTrie(TrieNode<InstrumentKey> node, string word, string companySymbol, string instrumentSymbol, string exchange) {
        foreach (char c in word) {
            if (!node.Children.ContainsKey(c))
                node.Children[c] = new();
            node = node.Children[c];
        }
        node.Keys.Add(new InstrumentKey(companySymbol, instrumentSymbol, exchange));
    }

    private void HandleQuickSearch(SearchServiceQuickSearchRequestInput inp, CancellationToken token) {
        const int MaxNumResults = 5;

        if (token.IsCancellationRequested)
            return;

        List<GetStockSearchResultsReplyItem> results = new();

        try {
            IEnumerable<InstrumentKey> symbols = SearchCompanies(inp.SearchTerm);
            foreach (InstrumentKey k in symbols) {
                InstrumentDto? dto = _registry.GetInstrument(k);

                if (dto is null)
                    continue;

                results.Add(new GetStockSearchResultsReplyItem() {
                    CompanyName = dto.CompanyName,
                    InstrumentSymbol = dto.InstrumentSymbol,
                    Exchange = dto.Exchange
                });

                if (results.Count == MaxNumResults)
                    break;
            }

            inp.Completed.TrySetResult(results);
        } catch (Exception ex) {
            _logger.LogError(ex, "HandleQuickSearch - general fault");
            inp.Completed.TrySetException(ex);
        }
    }

    private ISet<InstrumentKey> SearchCompanies(string searchTerm) {
        bool isSearchByExchange = searchTerm.Contains(':');
        TrieNode<InstrumentKey> node = isSearchByExchange ? _searchByExchangeRoot : _searchByCompanyRoot;

        foreach (char c in searchTerm.ToLower()) {
            if (!node!.Children.TryGetValue(c, out TrieNode<InstrumentKey>? child))
                return new HashSet<InstrumentKey>(); // No matches found
            node = child!;
        }

        var results = new HashSet<InstrumentKey>();
        CollectWords(node, results);

        return results;
    }

    private static void CollectWords(TrieNode<InstrumentKey> node, ISet<InstrumentKey> results) {
        if (node is null)
            return;

        foreach (InstrumentKey k in node.Keys) {
            results.Add(k);
        }

        foreach (TrieNode<InstrumentKey> child in node.Children.Values.Cast<TrieNode<InstrumentKey>>()) {
            CollectWords(child, results);
        }
    }

    private static async Task StartHeartbeat(IServiceProvider svp, CancellationToken ct) {
        ILogger logger = svp.GetRequiredService<ILogger<SearchService>>();

        while (!ct.IsCancellationRequested) {
            logger.LogInformation("SearchService heartbeat");
            await Task.Delay(Constants.OneMinute, ct);
        }
    }
}
