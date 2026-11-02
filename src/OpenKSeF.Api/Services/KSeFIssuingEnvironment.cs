using System.Security.Cryptography;
using KSeF.Client.Core.Interfaces.Services;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;

namespace OpenKSeF.Api.Services;

public sealed class KSeFIssuingEnvironment(ISystemConfigService systemConfig, IVerificationLinkService verificationLinks) : IKSeFIssuingEnvironment
{
    public string Current => DependencyInjection.NormalizeKSeFEnvironmentKey(
        systemConfig.GetValue(SystemConfigKeys.KSeFEnvironment) ?? systemConfig.GetValue(SystemConfigKeys.KSeFBaseUrl));

    public string BuildVerificationUrl(string sellerNip, DateOnly issueDate, byte[] invoiceXml) =>
        verificationLinks.BuildInvoiceVerificationUrl(
            sellerNip,
            issueDate.ToDateTime(TimeOnly.MinValue),
            Convert.ToBase64String(SHA256.HashData(invoiceXml)));
}
