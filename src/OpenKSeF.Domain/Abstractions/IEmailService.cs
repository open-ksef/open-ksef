using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;

namespace OpenKSeF.Domain.Abstractions;

public interface IEmailService
{
    Task SendNewInvoiceEmailAsync(string toEmail, NewInvoiceDetectedEvent evt);
    Task SendNewInvoicesSummaryEmailAsync(string toEmail, Guid tenantId, string company, int count);

    /// <returns>False when e-mail is disabled (Email:Enabled), so nothing was sent.</returns>
    Task<bool> SendInviteEmailAsync(string toEmail, Guid tenantId, string company, string nip, TenantRole role, string inviterName, string link, DateTime expiresAtUtc);
    Task<bool> SendAccessGrantedEmailAsync(string toEmail, Guid tenantId, string company, string nip, TenantRole role, string granterName, string portalUrl);
}
