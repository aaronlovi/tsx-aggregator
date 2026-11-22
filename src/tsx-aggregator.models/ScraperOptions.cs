using System;
using System.IO;

namespace tsx_aggregator.models;

/// <summary>
/// RawCollector browser settings, in the "Scraper" configuration section. With no settings, Puppeteer downloads its
/// own Chrome at run time (the dev setup). In a container image, Chrome is installed in the image and named here.
/// </summary>
public class ScraperOptions {
    public const string Scraper = "Scraper";

    /// <summary>
    /// Path of an installed Chrome (e.g. /usr/bin/google-chrome-stable). Empty: download one at run time.
    /// As an environment variable, Scraper__BrowserExecutablePath.
    /// </summary>
    public string? BrowserExecutablePath { get; set; }

    /// <summary>Extra Chrome command-line arguments, e.g. --no-sandbox in a container (Scraper__BrowserArgs__0, ...).</summary>
    public string[] BrowserArgs { get; set; } = [];

    public bool UsesInstalledBrowser => !string.IsNullOrWhiteSpace(BrowserExecutablePath);

    /// <returns>An error message, or null when the settings are valid.</returns>
    public string? Validate() {
        if (UsesInstalledBrowser && !File.Exists(BrowserExecutablePath))
            return $"{Scraper}:BrowserExecutablePath is set to '{BrowserExecutablePath}', but there is no such file";
        return null;
    }
}
