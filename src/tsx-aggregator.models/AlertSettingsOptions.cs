using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace tsx_aggregator.models;

public class AlertSettingsOptions {
    public const string AlertSettings = "AlertSettings";

    public AlertSettingsOptions() { }

    [Required(ErrorMessage = "SmtpHost is required in the AlertSettings section of the configuration")]
    public string SmtpHost { get; set; } = string.Empty;

    [Required(ErrorMessage = "SmtpPort is required in the AlertSettings section of the configuration")]
    public int SmtpPort { get; set; }

    [Required(ErrorMessage = "SmtpUsername is required in the AlertSettings section of the configuration")]
    public string SmtpUsername { get; set; } = string.Empty;

    [Required(ErrorMessage = "SmtpPassword is required in the AlertSettings section of the configuration")]
    public string SmtpPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "SenderEmail is required in the AlertSettings section of the configuration")]
    public string SenderEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Recipients is required in the AlertSettings section of the configuration")]
    public string[] Recipients { get; set; } = [];

    [Required(ErrorMessage = "CheckIntervalMinutes is required in the AlertSettings section of the configuration")]
    public int CheckIntervalMinutes { get; set; } = 60;

    public bool SendTestEmailOnStartup { get; set; }

    /// <summary>
    /// The recipient addresses: each Recipients entry split on commas and semicolons, trimmed, blanks dropped.
    /// The compose stack passes one setting (ALERT_RECIPIENT -> Recipients:0), so a list arrives comma-separated.
    /// </summary>
    public IReadOnlyList<string> RecipientAddresses =>
        (Recipients ?? [])
            .SelectMany(entry => (entry ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
}
