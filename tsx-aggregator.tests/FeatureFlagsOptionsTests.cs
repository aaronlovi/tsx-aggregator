using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using tsx_aggregator.models;

namespace tsx_aggregator.tests;

public class FeatureFlagsOptionsTests {

    [Fact]
    internal void CheckExistingRawReportUpdates_WhenNull_ShouldFailValidation() {
        // Arrange
        var model = new FeatureFlagsOptions();
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(model, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse();
        results.Should().ContainSingle();
        results[0].ErrorMessage.Should().Be("CheckExistingRawReportUpdates is required in the FeatureFlags section of the configuration");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CheckExistingRawReportUpdates_WhenNotNull_ShouldPassValidation(bool value) {
        // Arrange
        var model = new FeatureFlagsOptions { CheckExistingRawReportUpdates = value };
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(model, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }
}
