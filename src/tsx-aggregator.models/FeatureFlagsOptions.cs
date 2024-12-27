using System.ComponentModel.DataAnnotations;

namespace tsx_aggregator.models;

public class FeatureFlagsOptions {
    public const string FeatureFlags = "FeatureFlags";

    public FeatureFlagsOptions() { }

    /// <summary>
    /// Feature flag for checking incoming raw reports against existing raw reports and
    /// setting updated/conflicting reports to the side for manual review.
    /// </summary>
    [Required(ErrorMessage = "CheckExistingRawReportUpdates is required in the FeatureFlags section of the configuration")]
    public bool? CheckExistingRawReportUpdates { get; set; }
}
