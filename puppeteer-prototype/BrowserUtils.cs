using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using PuppeteerSharp;
using tsx_aggregator.shared;

namespace puppeteer_prototype;

// https://chromedevtools.github.io/devtools-protocol/1-3/Fetch/#type-RequestPattern
internal record FetchRequestPattern(string UrlPattern, string RequestStage = "Response");
internal record FetchRequestPatterns(FetchRequestPattern[] Patterns);
internal record RequestPausedCallbackParams(int RequestId, string Request, int ResponseStatusCode);

internal sealed class BrowserUtils : IAsyncDisposable {
    private static LaunchOptions _options = new() { Headless = false };
    private IBrowser? _browser;
    private IPage? _page;
    private ICDPSession? _cdpClient;
    private Action<RequestPausedCallbackParams>? _callbackFn;
    //private readonly ILogger<BrowserUtils> _logger;

    public static readonly string[] UrlPatternsToIntercept = {
        "*getEnhancedQuotes\\.json\\?symbols=*", "*getFinancialsEnhancedBySymbol\\.json\\?symbol=*"
    };


    public BrowserUtils(/*IServiceProvider svp*/) {
        //_logger = svp.GetRequiredService<ILogger<BrowserUtils>>();
    }


    public async Task Init(string[] urlPatternStrings, Action<RequestPausedCallbackParams> callbackFn) {
        if (_browser is not null)
            return;

        _callbackFn = callbackFn;

        await new BrowserFetcher().DownloadAsync(BrowserFetcher.DefaultChromiumRevision);

        try {
            _browser = await Puppeteer.LaunchAsync(_options);
            _page = await _browser.NewPageAsync();
            _cdpClient = await _page.Target.CreateCDPSessionAsync();

            // https://chromedevtools.github.io/devtools-protocol/1-3/Fetch/#method-enable
            // Enables issuing of requestPaused events. A request will be paused until client calls one of failRequest, fulfillRequest or continueRequest/continueWithAuth.
            var patternArr = urlPatternStrings.Select(p => new FetchRequestPattern(p)).ToArray();
            var patterns = new FetchRequestPatterns(patternArr);
            await _cdpClient.SendAsync("Fetch.enable", patterns);

            _cdpClient.MessageReceived += (sender, e) => {
                if (!e.MessageID.Equals("Fetch.requestPaused", StringComparison.OrdinalIgnoreCase))
                    return;

                
                Console.WriteLine(e.MessageData);
                foreach (JToken jsonObj in e.MessageData) {
                    if (jsonObj is JProperty prop) {

                    }
                }
            };

        } catch (Exception ex) {
            //_logger.LogError(ex, "Init failed");
            Console.WriteLine("Init failed - " + ex.Message);
        }
    }

    public async Task NavigateToPage(string url) {
        try {
            _ = await _page!.GoToAsync(url, WaitUntilNavigation.DOMContentLoaded);
        } catch (Exception ex) {
            Console.WriteLine($"NavigateToPage(url: {url}) Error - {ex.Message}");
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync() {
        if (_browser is not null)
            await _browser.CloseAsync();
        Utilities.SafeDispose(_browser);
        Utilities.SafeDispose(_page);
        Utilities.SafeDispose(_cdpClient);
    }
}
