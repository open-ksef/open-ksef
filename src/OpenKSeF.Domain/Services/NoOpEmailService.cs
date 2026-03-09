using Microsoft.Extensions.Logging;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Events;

namespace OpenKSeF.Domain.Services;

public class NoOpEmailService : IEmailService
{
    private readonly ILogger<NoOpEmailService> _logger;

    public NoOpEmailService(ILogger<NoOpEmailService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task SendNewInvoiceEmailAsync(string toEmail, NewInvoiceDetectedEvent evt)
    {
        _logger.LogDebug(
            "NoOpEmailService skipped email for {Email}, tenant {TenantId}, invoice {InvoiceId}",
            toEmail,
            evt.TenantId,
            evt.InvoiceId);

        return Task.CompletedTask;
    }
}
