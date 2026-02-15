using System.Threading;
using System.Threading.Tasks;

namespace tsx_aggregator.Services;

internal interface IEmailService {
    Task<bool> SendEmailAsync(string subject, string body, CancellationToken ct);
    Task<bool> SendEmailAsync(string subject, string plainBody, string? htmlBody, CancellationToken ct);
}
