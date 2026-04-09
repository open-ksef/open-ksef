#pragma warning disable CS0618 // ITransferDetailsService is a legacy interface that still works with InvoiceHeader
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Domain.Services;

/// <summary>
/// Formats bank transfer details from invoice metadata.
/// </summary>
public interface ITransferDetailsService
{
    /// <summary>
    /// Builds a copy-paste friendly text representation of transfer details.
    /// </summary>
    string BuildTransferText(InvoiceHeader invoice);

    /// <summary>
    /// Builds a structured transfer data object for QR code generation or programmatic use.
    /// </summary>
    TransferData BuildTransferData(InvoiceHeader invoice);
}
