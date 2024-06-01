using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using Microsoft.Extensions.Options;

namespace tsx_aggregator.models;

/// <summary>
/// Represents the Google Credentials configuration section in the application settings.
/// This class is used to map the related configuration values.
/// </summary>
/// <remarks>
/// The properties of this class correspond to the keys in the "GoogleCredentials" section of the appsettings.json file.
/// </remarks>
public class GoogleCredentialsOptions {
    public const string GoogleCredentials = "GoogleCredentials";

    public GoogleCredentialsOptions() {
        CredentialFilePath = string.Empty;
        GoogleApplicationName = string.Empty;
        SpreadsheetId = string.Empty;
        SpreadsheetName = string.Empty;
    }

    [Required(ErrorMessage = "CredentialFilePath is required in the GoogleCredentials section of the configuration")]
    public string CredentialFilePath { get; set; }
    
    [Required(ErrorMessage = "GoogleApplicationName is required in the GoogleCredentials section of the configuration")]
    public string GoogleApplicationName { get; set; }
    
    [Required(ErrorMessage = "SpreadsheetId is required in the GoogleCredentials section of the configuration")]
    public string SpreadsheetId { get; set; }
    
    [Required(ErrorMessage = "SpreadsheetName is required in the GoogleCredentials section of the configuration")]
    public string SpreadsheetName { get; set; }
}

/// <summary>
/// Validates the GoogleCredentialsOptions instance.
/// This class is used to ensure that the GoogleCredentialsOptions instance is correctly populated from the configuration.
/// </summary>
/// <remarks>
/// This class should be used in conjunction with the Options pattern in ASP.NET Core to validate the GoogleCredentialsOptions instance.
/// The Validate method should be called to perform the validation.
/// </remarks>
public class GoogleCredentialsOptionsValidator : IValidateOptions<GoogleCredentialsOptions> {
    public ValidateOptionsResult Validate(string? name, GoogleCredentialsOptions options) {
        string res = string.Empty;

        if (string.IsNullOrEmpty(options.CredentialFilePath))
            res += $"Missing '{nameof(GoogleCredentialsOptions.CredentialFilePath)}' in '{GoogleCredentialsOptions.GoogleCredentials}' section of app configuration \n";

        if (string.IsNullOrEmpty(options.GoogleApplicationName))
            res += $"Missing '{nameof(GoogleCredentialsOptions.GoogleApplicationName)}' in '{GoogleCredentialsOptions.GoogleCredentials}' section of app configuration \n";

        if (string.IsNullOrEmpty(options.SpreadsheetId))
            res += $"Missing '{nameof(GoogleCredentialsOptions.SpreadsheetId)}' in '{GoogleCredentialsOptions.GoogleCredentials}' section of app configuration \n";

        if (string.IsNullOrEmpty(options.SpreadsheetName))
            res += $"Missing '{nameof(GoogleCredentialsOptions.SpreadsheetName)}' in '{GoogleCredentialsOptions.GoogleCredentials}' section of app configuration \n";

        // Check if the file specified in 'CredentialFilePath' actually exists
        if (!File.Exists(options.CredentialFilePath))
            res += $"The file specified in '{nameof(GoogleCredentialsOptions.CredentialFilePath)}' in '{GoogleCredentialsOptions.GoogleCredentials}' does not exist \n";

        return string.IsNullOrEmpty(res) 
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(res);
    }
}
