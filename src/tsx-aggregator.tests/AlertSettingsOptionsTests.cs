using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using tsx_aggregator.models;

namespace tsx_aggregator.tests;

public class AlertSettingsOptionsTests {
    [Fact]
    public void AlertSettingsOptions_DefaultValues_ShouldBeInitialized() {
        // Arrange & Act
        var options = new AlertSettingsOptions();

        // Assert
        _ = options.SmtpHost.Should().BeEmpty();
        _ = options.SmtpPort.Should().Be(0);
        _ = options.SmtpUsername.Should().BeEmpty();
        _ = options.SmtpPassword.Should().BeEmpty();
        _ = options.SenderEmail.Should().BeEmpty();
        _ = options.Recipients.Should().BeEmpty();
        _ = options.CheckIntervalMinutes.Should().Be(60);
    }

    [Fact]
    public void AlertSettingsOptions_WithAllFieldsSet_ShouldPassValidation() {
        // Arrange
        var options = new AlertSettingsOptions {
            SmtpHost = "smtp.gmail.com",
            SmtpPort = 587,
            SmtpUsername = "user@gmail.com",
            SmtpPassword = "app-password",
            SenderEmail = "sender@gmail.com",
            Recipients = ["recipient@example.com"],
            CheckIntervalMinutes = 30
        };

        // Act
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        // Assert
        _ = isValid.Should().BeTrue();
        _ = results.Should().BeEmpty();
    }

    [Fact]
    public void AlertSettingsOptions_SectionName_ShouldBeAlertSettings() {
        _ = AlertSettingsOptions.AlertSettings.Should().Be("AlertSettings");
    }

    [Fact]
    public void HostedServicesOptions_RunTopScoreAlertService_ShouldExistAsNullableBool() {
        // Arrange
        var options = new HostedServicesOptions();

        // Assert
        _ = options.RunTopScoreAlertService.Should().BeNull();
    }

    [Fact]
    public void HostedServicesOptions_RunTopScoreAlertService_SetToTrue_ShouldReturnTrue() {
        // Arrange
        var options = new HostedServicesOptions { RunTopScoreAlertService = true };

        // Assert
        _ = options.RunTopScoreAlertService.Should().BeTrue();
    }

    [Fact]
    public void HostedServicesOptions_MissingRunTopScoreAlertService_ShouldFailValidation() {
        // Arrange
        var options = new HostedServicesOptions {
            RunAggregator = true,
            RunRawCollector = true,
            RunStocksDataRequestsProcessor = true,
            RunQuoteService = true,
            RunSearchService = true
            // RunTopScoreAlertService intentionally missing (null)
        };

        // Act
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        // Assert
        _ = isValid.Should().BeFalse();
        _ = results.Should().Contain(r => r.ErrorMessage!.Contains("RunTopScoreAlertService"));
    }
}
