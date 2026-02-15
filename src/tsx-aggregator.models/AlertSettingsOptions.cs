using System.ComponentModel.DataAnnotations;

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
}
