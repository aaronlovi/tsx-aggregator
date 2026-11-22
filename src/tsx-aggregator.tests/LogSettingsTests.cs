using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace tsx_aggregator.tests;

/// <summary>
/// D4: in a container logs go to stdout only (Logs:WriteFile=false); dev keeps the daily file. The startup
/// configuration log shows array settings (e.g. Scraper:BrowserArgs) with their values.
/// </summary>
public class LogSettingsTests {
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string NewTempDir() {
        string dir = Path.Combine(Path.GetTempPath(), "tsx-log-test-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteOneLine(IConfiguration config) {
        using var logger = (Serilog.Core.Logger)Program.CreateLogger(config);
        logger.Information("hello");
    }

    [Fact]
    public void Default_WritesTheDailyFile() {
        string dir = NewTempDir();
        try {
            WriteOneLine(Config(new() { ["Logs:Directory"] = dir }));

            string[] files = Directory.GetFiles(dir, "tsx-aggregator-*.txt");
            _ = files.Should().ContainSingle();
            _ = File.ReadAllText(files[0]).Should().Contain("hello");
        } finally {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WriteFileFalse_WritesNoFile() {
        string dir = NewTempDir();
        try {
            WriteOneLine(Config(new() { ["Logs:Directory"] = dir, ["Logs:WriteFile"] = "false" }));

            _ = Directory.GetFiles(dir).Should().BeEmpty();
        } finally {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WriteFileNotABoolean_FailsWithTheKeyName() {
        Action act = () => Program.CreateLogger(Config(new() { ["Logs:WriteFile"] = "nope" }));

        _ = act.Should().Throw<InvalidOperationException>().WithMessage("*Logs:WriteFile*");
    }

    [Fact]
    public void DescribeConfigSection_ShowsArrayValuesAndMasksSecrets() {
        IConfiguration config = Config(new() {
            ["Scraper:BrowserExecutablePath"] = "/usr/bin/google-chrome-stable",
            ["Scraper:BrowserArgs:0"] = "--no-sandbox",
            ["Scraper:BrowserArgs:1"] = "--disable-dev-shm-usage",
            ["AlertSettings:SmtpPassword"] = "hunter2",
        });

        var scraper = Program.DescribeConfigSection(config, "Scraper").ToDictionary(e => e.Key, e => e.Value);
        var alerts = Program.DescribeConfigSection(config, "AlertSettings").ToDictionary(e => e.Key, e => e.Value);

        _ = scraper["BrowserExecutablePath"].Should().Be("/usr/bin/google-chrome-stable");
        _ = scraper["BrowserArgs"].Should().Be("--no-sandbox, --disable-dev-shm-usage");
        _ = alerts["SmtpPassword"].Should().NotContain("hunter2");
    }
}
