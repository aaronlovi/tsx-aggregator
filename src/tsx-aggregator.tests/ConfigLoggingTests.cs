using FluentAssertions;

namespace tsx_aggregator.tests;

/// <summary>The startup "critical configuration" log must not print secrets.</summary>
public class ConfigLoggingTests {
    [Theory]
    [InlineData("SmtpPassword")]
    [InlineData("smtppassword")]
    [InlineData("ApiKey")]
    [InlineData("ClientSecret")]
    [InlineData("AccessToken")]
    public void SecretKey_WithValue_IsMasked(string key) {
        _ = Program.FormatConfigValueForLog(key, "hunter2").Should().Be("(set, masked)");
    }

    [Fact]
    public void SecretKey_WithoutValue_SaysNotSet() {
        _ = Program.FormatConfigValueForLog("SmtpPassword", "").Should().Be("(not set)");
        _ = Program.FormatConfigValueForLog("SmtpPassword", null).Should().Be("(not set)");
    }

    [Fact]
    public void OtherKey_IsShownAsIs() {
        _ = Program.FormatConfigValueForLog("SmtpHost", "smtp-mail.outlook.com").Should().Be("smtp-mail.outlook.com");
    }
}
