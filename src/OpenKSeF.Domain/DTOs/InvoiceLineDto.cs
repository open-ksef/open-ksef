namespace OpenKSeF.Domain.DTOs;

public record InvoiceLineDto(
    int LineNumber,
    string? Name,
    string? Unit,
    decimal? Quantity,
    decimal? UnitPriceNet,
    decimal? UnitPriceGross,
    decimal? AmountNet,
    decimal? AmountGross,
    decimal? AmountVat,
    string? VatRate);
