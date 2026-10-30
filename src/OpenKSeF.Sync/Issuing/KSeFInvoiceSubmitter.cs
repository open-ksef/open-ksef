using System.Net;
using System.Text;
using KSeF.Client.Api.Builders.Online;
using KSeF.Client.Core.Exceptions;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.Sessions;
using OpenKSeF.Domain.Abstractions;

namespace OpenKSeF.Sync.Issuing;

/// <summary>Online-session send of FA(3) invoices through CIRFMF KSeF.Client.</summary>
public sealed class KSeFInvoiceSubmitter(IKSeFClient client, ICryptographyService cryptography) : IKSeFInvoiceSubmitter
{
    public async Task<KSeFOnlineSession> OpenSessionAsync(string accessToken, CancellationToken ct = default)
    {
        var encryption = cryptography.GetEncryptionData();
        var request = OpenOnlineSessionRequestBuilder.Create()
            .WithFormCode(systemCode: "FA (3)", schemaVersion: "1-0E", value: "FA")
            .WithEncryption(
                encryptedSymmetricKey: encryption.EncryptionInfo.EncryptedSymmetricKey,
                initializationVector: encryption.EncryptionInfo.InitializationVector,
                publicKeyId: encryption.EncryptionInfo.PublicKeyId)
            .Build();
        var session = await client.OpenOnlineSessionAsync(request, accessToken, cancellationToken: ct);
        return new KSeFOnlineSession(session.ReferenceNumber, accessToken, encryption.CipherKey, encryption.CipherIv);
    }

    public async Task<string> SendAsync(KSeFOnlineSession session, byte[] invoiceXml, CancellationToken ct = default)
    {
        var encrypted = cryptography.EncryptBytesWithAES256(invoiceXml, session.CipherKey, session.CipherIv);
        var plainMeta = cryptography.GetMetaData(invoiceXml);
        var encryptedMeta = cryptography.GetMetaData(encrypted);
        var request = SendInvoiceOnlineSessionRequestBuilder.Create()
            .WithInvoiceHash(plainMeta.HashSHA, plainMeta.FileSize)
            .WithEncryptedDocumentHash(encryptedMeta.HashSHA, encryptedMeta.FileSize)
            .WithEncryptedDocumentContent(Convert.ToBase64String(encrypted))
            .Build();
        var sent = await client.SendOnlineSessionInvoiceAsync(request, session.ReferenceNumber, session.AccessToken, ct);
        return sent.ReferenceNumber;
    }

    public async Task<KSeFSentInvoice?> GetInvoiceAsync(string accessToken, string sessionRef, string elementRef, CancellationToken ct = default)
    {
        try
        {
            return Map(await client.GetSessionInvoiceAsync(sessionRef, elementRef, accessToken, ct));
        }
        catch (KsefApiException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<KSeFSentInvoice>> ListInvoicesAsync(string accessToken, string sessionRef, CancellationToken ct = default)
    {
        var invoices = new List<KSeFSentInvoice>();
        string? continuation = null;
        do
        {
            var page = await client.GetSessionInvoicesAsync(sessionRef, accessToken, pageSize: 100, continuationToken: continuation, cancellationToken: ct);
            invoices.AddRange(page.Invoices?.Select(Map) ?? []);
            continuation = page.ContinuationToken;
        }
        while (!string.IsNullOrEmpty(continuation));

        return invoices;
    }

    public async Task<byte[]?> GetUpoAsync(string accessToken, string sessionRef, string elementRef, CancellationToken ct = default)
    {
        try
        {
            var upo = await client.GetSessionInvoiceUpoByReferenceNumberAsync(sessionRef, elementRef, accessToken, ct);
            return string.IsNullOrEmpty(upo) ? null : Encoding.UTF8.GetBytes(upo);
        }
        catch (KsefApiException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task CloseSessionAsync(KSeFOnlineSession session, CancellationToken ct = default) =>
        client.CloseOnlineSessionAsync(session.ReferenceNumber, session.AccessToken, ct);

    private static KSeFSentInvoice Map(SessionInvoice invoice) => new(
        invoice.ReferenceNumber,
        invoice.InvoiceHash,
        invoice.Status?.Code ?? KSeFSentInvoice.Processing,
        invoice.Status?.Description,
        invoice.Status?.Details?.ToList() ?? [],
        invoice.KsefNumber,
        invoice.PermanentStorageDate ?? invoice.AcquisitionDate);
}
