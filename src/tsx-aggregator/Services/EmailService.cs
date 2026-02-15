using System;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using tsx_aggregator.models;

namespace tsx_aggregator.Services;

internal class EmailService : IEmailService {
    private readonly AlertSettingsOptions _settings;
    private readonly ILogger _logger;

    public EmailService(IOptions<AlertSettingsOptions> options, ILogger<EmailService> logger) {
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<bool> SendEmailAsync(string subject, string body, CancellationToken ct) {
        try {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.SenderEmail, _settings.SenderEmail));

            foreach (var recipient in _settings.Recipients)
                message.To.Add(new MailboxAddress(recipient, recipient));

            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            using var client = new SmtpClient();
            await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(_settings.SmtpUsername, _settings.SmtpPassword, ct);
            _ = await client.SendAsync(message, ct);
            await client.DisconnectAsync(quit: true, ct);

            _logger.LogInformation("Score-13 alert email sent successfully to {RecipientCount} recipients", _settings.Recipients.Length);
            return true;
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to send score-13 alert email");
            return false;
        }
    }
}
