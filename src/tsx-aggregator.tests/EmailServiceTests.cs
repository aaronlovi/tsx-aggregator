using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using tsx_aggregator.models;
using tsx_aggregator.Services;

namespace tsx_aggregator.tests;

public class EmailServiceTests {

    private static IOptions<AlertSettingsOptions> CreateOptions(
        string smtpHost = "smtp.example.com",
        int smtpPort = 587,
        string smtpUsername = "user@example.com",
        string smtpPassword = "password",
        string senderEmail = "sender@example.com",
        string[]? recipients = null) {

        var options = new AlertSettingsOptions {
            SmtpHost = smtpHost,
            SmtpPort = smtpPort,
            SmtpUsername = smtpUsername,
            SmtpPassword = smtpPassword,
            SenderEmail = senderEmail,
            Recipients = recipients ?? ["recipient@example.com"],
            CheckIntervalMinutes = 60
        };

        return Options.Create(options);
    }

    [Fact]
    public void Constructor_WithValidOptions_DoesNotThrow() {
        // Arrange
        var options = CreateOptions();
        var logger = new Mock<ILogger<EmailService>>();

        // Act & Assert
        var service = new EmailService(options, logger.Object);
        _ = service.Should().NotBeNull();
    }

    [Fact]
    public async Task SendEmailAsync_WithUnreachableHost_ReturnsFalse() {
        // Arrange
        var options = CreateOptions(smtpHost: "unreachable.invalid.host.example.com", smtpPort: 12345);
        var logger = new Mock<ILogger<EmailService>>();
        var service = new EmailService(options, logger.Object);

        // Act
        var result = await service.SendEmailAsync("Test Subject", "Test Body", CancellationToken.None);

        // Assert
        _ = result.Should().BeFalse("the SMTP host is unreachable");
    }

    [Fact]
    public async Task SendEmailAsync_WithCancelledToken_ReturnsFalse() {
        // Arrange
        var options = CreateOptions();
        var logger = new Mock<ILogger<EmailService>>();
        var service = new EmailService(options, logger.Object);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await service.SendEmailAsync("Test Subject", "Test Body", cts.Token);

        // Assert
        _ = result.Should().BeFalse("the cancellation token was already cancelled");
    }
}
