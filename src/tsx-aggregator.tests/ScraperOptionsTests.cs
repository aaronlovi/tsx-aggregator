using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using PuppeteerSharp;
using tsx_aggregator.models;

namespace tsx_aggregator.tests;

/// <summary>
/// D3: in a container the scraper uses an installed Chrome (Scraper:BrowserExecutablePath) with extra arguments,
/// instead of downloading one at run time. With no settings it behaves as before.
/// </summary>
public class ScraperOptionsTests {
    private static ScraperOptions Bind(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build()
            .GetSection(ScraperOptions.Scraper).Get<ScraperOptions>() ?? new ScraperOptions();

    [Fact]
    public void NoSettings_DownloadsAndLaunchesHeadlessAsBefore() {
        var options = new ScraperOptions();

        LaunchOptions launch = TsxCompanyProcessor.BuildLaunchOptions(options);

        _ = options.UsesInstalledBrowser.Should().BeFalse();
        _ = launch.Headless.Should().BeTrue();
        _ = launch.ExecutablePath.Should().BeNull();
        _ = launch.Args.Should().BeEmpty();
        _ = options.Validate().Should().BeNull();
    }

    [Fact]
    public void InstalledBrowser_LaunchesItWithTheArguments() {
        ScraperOptions options = Bind(new() {
            ["Scraper:BrowserExecutablePath"] = "/usr/bin/google-chrome-stable",
            ["Scraper:BrowserArgs:0"] = "--no-sandbox",
            ["Scraper:BrowserArgs:1"] = "--disable-dev-shm-usage",
        });

        LaunchOptions launch = TsxCompanyProcessor.BuildLaunchOptions(options);

        _ = options.UsesInstalledBrowser.Should().BeTrue();
        _ = launch.Headless.Should().BeTrue();
        _ = launch.ExecutablePath.Should().Be("/usr/bin/google-chrome-stable");
        _ = launch.Args.Should().Equal("--no-sandbox", "--disable-dev-shm-usage");
    }

    [Fact]
    public void BlankPath_CountsAsNotSet() {
        // An empty environment variable (Scraper__BrowserExecutablePath=) must not disable the download.
        ScraperOptions options = Bind(new() { ["Scraper:BrowserExecutablePath"] = "  " });

        _ = options.UsesInstalledBrowser.Should().BeFalse();
        _ = TsxCompanyProcessor.BuildLaunchOptions(options).ExecutablePath.Should().BeNull();
        _ = options.Validate().Should().BeNull();
    }

    [Fact]
    public void PathToMissingFile_IsAnError() {
        var options = new ScraperOptions { BrowserExecutablePath = "/nonexistent/chrome" };

        _ = options.Validate().Should().Contain("Scraper:BrowserExecutablePath").And.Contain("/nonexistent/chrome");
    }

    [Fact]
    public void PathToExistingFile_IsValid() {
        string file = Path.GetTempFileName();
        try {
            _ = new ScraperOptions { BrowserExecutablePath = file }.Validate().Should().BeNull();
        } finally {
            File.Delete(file);
        }
    }
}
