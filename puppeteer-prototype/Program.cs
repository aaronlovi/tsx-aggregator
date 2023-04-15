using System;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace puppeteer_prototype;

internal class Program {
    static async Task Main(string[] args) {
        var options = new LaunchOptions { Headless = true };
        Console.WriteLine("Downloading chromium");

        await new BrowserFetcher().DownloadAsync(BrowserFetcher.DefaultChromiumRevision);
        Console.WriteLine("Navigating to developers.google.com");

        await using var browser = new BrowserUtils();
        await browser.Init(BrowserUtils.UrlPatternsToIntercept, x => { });
        await browser.NavigateToPage("https://money.tmx.com/en/quote/BMO/financials-filings");

        Console.WriteLine("Waiting 1 minute for response");
        await Task.Delay(TimeSpan.FromMinutes(1));
        Console.WriteLine("Done waiting");

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
}
