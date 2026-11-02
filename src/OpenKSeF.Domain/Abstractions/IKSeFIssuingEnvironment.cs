namespace OpenKSeF.Domain.Abstractions;

/// <summary>Current KSeF environment and KOD I link for issued invoices; the KSeF.Client side stays out of Domain.</summary>
public interface IKSeFIssuingEnvironment
{
    string Current { get; }

    string BuildVerificationUrl(string sellerNip, DateOnly issueDate, byte[] invoiceXml);
}
