using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using tsx_aggregator.models;
using tsx_aggregator.Services;
using tsx_aggregator.shared;

namespace tsx_aggregator.tests;

public class Score13AlertServiceTests {

    private static AlertSettingsOptions CreateValidSettings() => new() {
        SmtpHost = "smtp.example.com",
        SmtpPort = 587,
        SmtpUsername = "user@example.com",
        SmtpPassword = "password",
        SenderEmail = "sender@example.com",
        Recipients = ["recipient@example.com"],
        CheckIntervalMinutes = 1
    };

    private static GetStocksDataReply CreateReplyWithItems(params (string symbol, string company)[] items) {
        var reply = new GetStocksDataReply { Success = true };
        foreach (var (symbol, company) in items) {
            reply.StocksData.Add(new GetStocksDataReplyItem {
                Exchange = "TSX",
                CompanySymbol = symbol.Replace(".TO", ""),
                InstrumentSymbol = symbol,
                CompanyName = company,
                InstrumentName = $"{company} Common Shares",
                CurrentLongTermDebt = 100_000_000,
                CurrentTotalShareholdersEquity = 500_000_000,
                CurrentBookValue = 200_000_000,
                CurrentNumShares = 10_000_000,
                AverageNetCashFlow = 40_000_000,
                AverageOwnerEarnings = 35_000_000,
                CurrentDividendsPaid = 5_000_000,
                CurrentAdjustedRetainedEarnings = 50_000_000,
                OldestRetainedEarnings = 30_000_000,
                NumAnnualProcessedCashFlowReports = 5,
                PerSharePrice = 50M
            });
        }
        return reply;
    }

    private static Mock<IStocksDataRequestsProcessor> SetupRequestProcessor(GetStocksDataReply reply) {
        var mock = new Mock<IStocksDataRequestsProcessor>();
        _ = mock.Setup(m => m.PostRequest(It.IsAny<StocksDataRequestsInputBase>()))
            .Returns((StocksDataRequestsInputBase input) => {
                _ = Task.Run(() => input.Completed.SetResult(reply));
                return true;
            });
        return mock;
    }

    private static Mock<IQuoteService> SetupQuoteService(IDictionary<string, decimal> prices) {
        var mock = new Mock<IQuoteService>();
        _ = mock.Setup(m => m.QuoteServiceReady).Returns(new TaskCompletionSource());
        _ = mock.Setup(m => m.PostRequest(It.IsAny<QuoteServiceInputBase>()))
            .Returns((QuoteServiceInputBase input) => {
                _ = Task.Run(() => input.Completed.SetResult(prices));
                return true;
            });
        // Mark QuoteService as ready
        mock.Object.QuoteServiceReady.TrySetResult();
        return mock;
    }

    [Fact]
    public void Constructor_WithMockedDependencies_DoesNotThrow() {
        // Arrange
        var requestProcessor = new Mock<IStocksDataRequestsProcessor>();
        var quotesService = new Mock<IQuoteService>();
        _ = quotesService.Setup(m => m.QuoteServiceReady).Returns(new TaskCompletionSource());
        var emailService = new Mock<IEmailService>();
        var options = Options.Create(CreateValidSettings());
        var logger = new Mock<ILogger<Score13AlertService>>();

        // Act & Assert
        var service = new Score13AlertService(
            requestProcessor.Object, quotesService.Object, emailService.Object, options, logger.Object);
        _ = service.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptySmtpHost_ExitsGracefully() {
        // Arrange
        var requestProcessor = new Mock<IStocksDataRequestsProcessor>();
        var quotesService = new Mock<IQuoteService>();
        _ = quotesService.Setup(m => m.QuoteServiceReady).Returns(new TaskCompletionSource());
        var emailService = new Mock<IEmailService>();
        var settings = CreateValidSettings();
        settings.SmtpHost = "";
        var options = Options.Create(settings);
        var logger = new Mock<ILogger<Score13AlertService>>();

        var service = new Score13AlertService(
            requestProcessor.Object, quotesService.Object, emailService.Object, options, logger.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        // Assert - no email should be sent, no crash
        emailService.Verify(m => m.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_FirstRun_EstablishesBaselineNoEmailSent() {
        // Arrange
        var reply = CreateReplyWithItems(("ABC.TO", "ABC Corp"), ("DEF.TO", "DEF Inc"));
        var prices = new Dictionary<string, decimal> { ["ABC.TO"] = 50M, ["DEF.TO"] = 50M };

        var requestProcessor = SetupRequestProcessor(reply);
        var quotesService = SetupQuoteService(prices);
        var emailService = new Mock<IEmailService>();
        var options = Options.Create(CreateValidSettings());
        var logger = new Mock<ILogger<Score13AlertService>>();

        var service = new Score13AlertService(
            requestProcessor.Object, quotesService.Object, emailService.Object, options, logger.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StartAsync(cts.Token);
        await Task.Delay(500);
        await service.StopAsync(CancellationToken.None);

        // Assert - first run is baseline, no email
        emailService.Verify(m => m.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoChangeInList_NoEmailSent() {
        // Arrange - return same data every time
        var reply = CreateReplyWithItems(("ABC.TO", "ABC Corp"));
        var prices = new Dictionary<string, decimal> { ["ABC.TO"] = 50M };

        int callCount = 0;
        var requestProcessor = new Mock<IStocksDataRequestsProcessor>();
        _ = requestProcessor.Setup(m => m.PostRequest(It.IsAny<StocksDataRequestsInputBase>()))
            .Returns((StocksDataRequestsInputBase input) => {
                callCount++;
                // Return a fresh copy each time (gRPC reply is mutable)
                var freshReply = CreateReplyWithItems(("ABC.TO", "ABC Corp"));
                _ = Task.Run(() => input.Completed.SetResult(freshReply));
                return true;
            });

        var quotesService = SetupQuoteService(prices);
        var emailService = new Mock<IEmailService>();
        var settings = CreateValidSettings();
        settings.CheckIntervalMinutes = 0; // No delay between checks for testing
        var options = Options.Create(settings);
        var logger = new Mock<ILogger<Score13AlertService>>();

        var service = new Score13AlertService(
            requestProcessor.Object, quotesService.Object, emailService.Object, options, logger.Object);

        // Act - run long enough for at least 2 cycles
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await service.StartAsync(cts.Token);
        await Task.Delay(1500);
        await service.StopAsync(CancellationToken.None);

        // Assert - should have at least 2 calls but no email since list didn't change
        _ = callCount.Should().BeGreaterThanOrEqualTo(2);
        emailService.Verify(m => m.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ListChanges_SendsEmailWithCorrectSubject() {
        // Arrange - first call returns ABC, second call returns ABC + DEF
        int callCount = 0;
        var requestProcessor = new Mock<IStocksDataRequestsProcessor>();
        _ = requestProcessor.Setup(m => m.PostRequest(It.IsAny<StocksDataRequestsInputBase>()))
            .Returns((StocksDataRequestsInputBase input) => {
                callCount++;
                GetStocksDataReply reply = callCount == 1
                    ? CreateReplyWithItems(("ABC.TO", "ABC Corp"))
                    : CreateReplyWithItems(("ABC.TO", "ABC Corp"), ("DEF.TO", "DEF Inc"));
                _ = Task.Run(() => input.Completed.SetResult(reply));
                return true;
            });

        var prices = new Dictionary<string, decimal> { ["ABC.TO"] = 50M, ["DEF.TO"] = 50M };
        var quotesService = SetupQuoteService(prices);
        var emailService = new Mock<IEmailService>();
        _ = emailService.Setup(m => m.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var settings = CreateValidSettings();
        settings.CheckIntervalMinutes = 0;
        var options = Options.Create(settings);
        var logger = new Mock<ILogger<Score13AlertService>>();

        var service = new Score13AlertService(
            requestProcessor.Object, quotesService.Object, emailService.Object, options, logger.Object);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await service.StartAsync(cts.Token);
        await Task.Delay(1500);
        await service.StopAsync(CancellationToken.None);

        // Assert
        emailService.Verify(m => m.SendEmailAsync(
            It.Is<string>(s => s.Contains("1 added") && s.Contains("0 removed")),
            It.Is<string>(b => b.Contains("DEF.TO")),
            It.Is<string?>(h => h != null && h.Contains("DEF.TO") && h.Contains("<table")),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_RequestProcessorFails_DoesNotCrash() {
        // Arrange
        var requestProcessor = new Mock<IStocksDataRequestsProcessor>();
        _ = requestProcessor.Setup(m => m.PostRequest(It.IsAny<StocksDataRequestsInputBase>()))
            .Returns(false); // Simulate failure

        var quotesService = new Mock<IQuoteService>();
        _ = quotesService.Setup(m => m.QuoteServiceReady).Returns(new TaskCompletionSource());
        quotesService.Object.QuoteServiceReady.TrySetResult();
        var emailService = new Mock<IEmailService>();
        var settings = CreateValidSettings();
        settings.CheckIntervalMinutes = 0;
        var options = Options.Create(settings);
        var logger = new Mock<ILogger<Score13AlertService>>();

        var service = new Score13AlertService(
            requestProcessor.Object, quotesService.Object, emailService.Object, options, logger.Object);

        // Act - should not throw
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StartAsync(cts.Token);
        await Task.Delay(500);
        await service.StopAsync(CancellationToken.None);

        // Assert - no email, no crash
        emailService.Verify(m => m.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
