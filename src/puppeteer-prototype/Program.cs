using System;
using System.Net;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace puppeteer_prototype;

internal class Program {
    static async Task Main(string[] args) {
        await SimulateFetchFinancials();
        return;

        //var options = new LaunchOptions { Headless = true };
        //Console.WriteLine("Downloading chromium");

        //await new BrowserFetcher().DownloadAsync(BrowserFetcher.DefaultChromiumRevision);
        //Console.WriteLine("Navigating to developers.google.com");

        //await using var browser = new BrowserUtils();
        //await browser.Init(BrowserUtils.UrlPatternsToIntercept, x => { });
        //await browser.NavigateToPage("https://money.tmx.com/en/quote/BMO/financials-filings");

        //Console.WriteLine("Waiting 1 minute for response");
        //await Task.Delay(TimeSpan.FromMinutes(1));
        //Console.WriteLine("Done waiting");

        //using var browser = await Puppeteer.LaunchAsync(options);
        //using var page = await browser.NewPageAsync();

        //await page.GoToAsync("https://developers.google.com/web/");
//        // Type into search box.
//        // await page.TypeAsync("#searchbox input", "Headless Chrome");
//        await page.TypeAsync(".devsite-searchbox input", "Headless Chrome");

//        // Wait for suggest overlay to appear and click "show all results".
//        var allResultsSelector = ".devsite-suggest-all-results";
//        await page.WaitForSelectorAsync(allResultsSelector);
//        await page.ClickAsync(allResultsSelector);

//        // Wait for the results page to load and display the results.
//        var resultsSelector = ".gsc-results .gsc-thumbnail-inside a.gs-title";
//        await page.WaitForSelectorAsync(resultsSelector);
//        var links = await page.EvaluateFunctionAsync(@"(resultsSelector) => {
//    const anchors = Array.from(document.querySelectorAll(resultsSelector));
//    return anchors.map(anchor => {
//      const title = anchor.textContent.split('|')[0].trim();
//      return `${title} - ${anchor.href}`;
//    });
//}", resultsSelector);

//        foreach (var link in links)
//            Console.WriteLine(link);

//        Console.WriteLine("Press any key to continue...");
//        Console.ReadLine();
    }

    static async Task SimulateFetchFinancials() {
        Console.WriteLine("Downloading Chromium");
        await DownloadChromium();
        
        Console.WriteLine("Launching Chromium");
        var browser = await Puppeteer.LaunchAsync(new LaunchOptions {
            Headless = true
        });
        using IPage page = await browser.NewPageAsync();
        page.DefaultNavigationTimeout = 60000;
        page.Response += ProcessPageResponse;
        await page.GoToAsync("https://money.tmx.com/en/quote/BMO/financials-filings");

        Console.WriteLine("Waiting 1 minute for response");
        await Task.Delay(TimeSpan.FromMinutes(1));
        Console.WriteLine("Done waiting");
    }

    private static async Task DownloadChromium() {
        using var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync();
    }

    private static async void ProcessPageResponse(object? sender, ResponseCreatedEventArgs e) {
        if (!e.Response.Ok)
            return;

        if (!e.Response.Url.Contains("getEnhancedQuotes.json")
            && !e.Response.Url.Contains("getFinancialsEnhancedBySymbol.json"))
            return;

        Console.WriteLine("Found URL: {0}", e.Response.Url);

        var headers = e.Response.Headers;
        if (!headers.ContainsKey("content-encoding")) {
            Console.WriteLine("No content, aborting");
            return;
        }

        try {
            string responseText = await e.Response.TextAsync();
            Console.WriteLine("Repsonse {0} received for {1}", responseText.Length, e.Response.Url);
        } catch (Exception ex) {
            Console.WriteLine("Error when reading response body for URL {0} - {1}", e.Response.Url, ex.Message);
        }
    }
}
