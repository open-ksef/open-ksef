using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Domain.Services;

/// <summary>REQ-0040 Part 2: one company's accepted sales and received purchase XML, UPO, indeks.csv and README.txt as a ZIP written while reading; reads only.</summary>
public sealed class InvoiceExportService(ApplicationDbContext db, IKSeFIssuingEnvironment environment, TimeProvider time)
{
    private const string Sales = "sprzedaz";
    private const string Purchases = "zakup";
    private const string IndexHeader = "kierunek;numer_ksef;numer_faktury;rodzaj;data_wystawienia;data_przyjecia_ksef;nip_sprzedawcy;nazwa_sprzedawcy;id_nabywcy;nazwa_nabywcy;waluta;netto;vat;brutto;termin_platnosci;oplacona;plik_xml;plik_upo";

    public async Task<InvoiceExport> PrepareAsync(Guid tenantId, DateOnly? from, DateOnly? to, bool sales, bool purchases, CancellationToken ct)
    {
        var nip = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Nip).SingleAsync(ct);
        var request = new InvoiceExportRequest(tenantId, from, to, sales, purchases);
        var rows = new List<InvoiceExportRow>();

        if (sales)
        {
            var issued = await AcceptedIssued(request)
                .Select(i => new InvoiceExportRow(i.Id, true, Sales, i.KsefNumber!, i.Number, i.InvoiceType.Code(), i.IssueDate,
                    i.AcceptedAt == null ? null : IssuedInvoice.PolandDate(i.AcceptedAt.Value), i.SellerNip, i.SellerName,
                    IssuedBuyerId(i.BuyerKind, i.BuyerIdType, i.BuyerNip, i.BuyerCountryCode, i.BuyerForeignId), i.BuyerName,
                    i.Currency, i.TotalNet, i.TotalVat, i.TotalGross, i.DueDate, null))
                .ToListAsync(ct);
            var issuedNumbers = issued.Select(r => r.KsefNumber).ToHashSet(StringComparer.Ordinal);
            rows.AddRange(issued);
            rows.AddRange((await HeaderRows(Headers(request, InvoiceDirection.Issued), Sales, ct)).Where(r => !issuedNumbers.Contains(r.KsefNumber)));
        }

        if (purchases)
            rows.AddRange(await HeaderRows(Headers(request, InvoiceDirection.Received), Purchases, ct));

        var suffix = environment.Current switch { "production" => "", "demo" => "_DEMO", _ => "_TEST" };
        var period = from is null ? "wszystko" : $"{Date(from)}_{Date(to)}";
        return new InvoiceExport(request, nip, $"OpenKSeF_{nip}_{period}{suffix}.zip",
            rows.OrderBy(r => r.Direction == Purchases).ThenBy(r => r.IssueDate).ThenBy(r => r.KsefNumber, StringComparer.Ordinal).ToList());
    }

    public async Task WriteAsync(InvoiceExport export, Stream output, CancellationToken ct)
    {
        var request = export.Request;
        var rows = export.Rows.ToDictionary(r => r.Id);
        await using var zip = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, ct);
        await WriteTextAsync(zip, "README.txt", Readme(export), ct);

        if (request.Sales)
        {
            await foreach (var document in AcceptedIssued(request).Select(i => new { i.Id, i.SentXml, i.UpoXml }).AsAsyncEnumerable().WithCancellation(ct))
            {
                if (!rows.TryGetValue(document.Id, out var row) || !row.FromIssuing)
                    continue;
                if (document.SentXml is not null)
                    row.XmlFile = await WriteFileAsync(zip, $"{Sales}/{row.KsefNumber}.xml", document.SentXml, ct);
                if (document.UpoXml is not null)
                    row.UpoFile = await WriteFileAsync(zip, $"{Sales}/{row.KsefNumber}_UPO.xml", document.UpoXml, ct);
            }

            await WriteStoredXmlAsync(zip, Headers(request, InvoiceDirection.Issued), rows, ct);
        }

        if (request.Purchases)
            await WriteStoredXmlAsync(zip, Headers(request, InvoiceDirection.Received), rows, ct);

        await using var index = await zip.CreateEntry("indeks.csv", CompressionLevel.Fastest).OpenAsync(ct);
        await using var writer = new StreamWriter(index, new UTF8Encoding(true), leaveOpen: true) { NewLine = "\r\n" };
        await writer.WriteLineAsync(IndexHeader);
        foreach (var r in export.Rows)
        {
            await writer.WriteLineAsync(string.Join(';',
                r.Direction, Text(r.KsefNumber), Text(r.Number), Text(r.Type), Date(r.IssueDate), Date(r.AcceptedOn),
                Text(r.SellerNip), Text(r.SellerName), Text(r.BuyerId), Text(r.BuyerName), Text(r.Currency),
                Amount(r.Net), Amount(r.Vat), Amount(r.Gross), Date(r.DueDate), r.Paid switch { true => "tak", false => "nie", null => "" },
                Text(r.XmlFile), Text(r.UpoFile)));
        }
    }

    private IQueryable<IssuedInvoice> AcceptedIssued(InvoiceExportRequest request)
    {
        var query = db.IssuedInvoices.Where(i => i.TenantId == request.TenantId && i.Status == IssuedInvoiceStatus.Accepted);
        if (request.From is { } from)
            query = query.Where(i => i.IssueDate >= from);
        if (request.To is { } to)
            query = query.Where(i => i.IssueDate <= to);
        return query;
    }

    private IQueryable<InvoiceHeader> Headers(InvoiceExportRequest request, InvoiceDirection direction)
    {
        var query = db.InvoiceHeaders.Where(h => h.TenantId == request.TenantId && h.Direction == direction);
        if (request.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(h => h.IssueDate >= start);
        }

        if (request.To is { } to)
        {
            var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(h => h.IssueDate < end);
        }

        return query;
    }

    private static Task<List<InvoiceExportRow>> HeaderRows(IQueryable<InvoiceHeader> headers, string direction, CancellationToken ct) =>
        headers
            .Select(h => new InvoiceExportRow(h.Id, false, direction, h.KSeFInvoiceNumber, h.InvoiceNumber, HeaderType(h.InvoiceType),
                DateOnly.FromDateTime(h.IssueDate), h.AcquisitionDate == null ? null : IssuedInvoice.PolandDate(h.AcquisitionDate.Value),
                h.VendorNip, h.VendorName, h.BuyerNip ?? h.BuyerForeignId, h.BuyerName, h.Currency, h.AmountNet, h.AmountVat, h.AmountGross,
                h.DueDate, direction == Purchases ? h.IsPaid : null))
            .ToListAsync(ct);

    private static async Task WriteStoredXmlAsync(ZipArchive zip, IQueryable<InvoiceHeader> headers, Dictionary<Guid, InvoiceExportRow> rows, CancellationToken ct)
    {
        await foreach (var xml in headers.Where(h => h.Xml != null).Select(h => new { h.Id, h.Xml!.Content }).AsAsyncEnumerable().WithCancellation(ct))
        {
            if (rows.TryGetValue(xml.Id, out var row) && !row.FromIssuing)
                row.XmlFile = await WriteFileAsync(zip, $"{row.Direction}/{row.KsefNumber}.xml", xml.Content, ct);
        }
    }

    private static async Task<string> WriteFileAsync(ZipArchive zip, string name, byte[] content, CancellationToken ct)
    {
        await using var entry = await zip.CreateEntry(name, CompressionLevel.Fastest).OpenAsync(ct);
        await entry.WriteAsync(content, ct);
        return name;
    }

    private static async Task WriteTextAsync(ZipArchive zip, string name, string text, CancellationToken ct)
    {
        await using var entry = await zip.CreateEntry(name, CompressionLevel.Fastest).OpenAsync(ct);
        await entry.WriteAsync(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray(), ct);
    }

    private string Readme(InvoiceExport export)
    {
        var request = export.Request;
        var period = request.From is null ? "wszystkie faktury" : $"{Date(request.From)} - {Date(request.To)}";
        var directions = string.Join(", ", new[] { request.Sales ? "sprzedaż" : null, request.Purchases ? "zakup" : null }.OfType<string>());
        var ksef = environment.Current switch
        {
            "production" => "produkcyjne",
            "demo" => "przedprodukcyjne (DEMO)",
            _ => "testowe (TEST)",
        };
        return $$"""
            Eksport faktur z OpenKSeF

            NIP firmy: {{export.Nip}}
            Okres (data wystawienia): {{period}}
            Kierunki: {{directions}}
            Środowisko KSeF: {{ksef}}
            Utworzono: {{time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}} UTC

            Zawartość archiwum:
            - sprzedaz/{numer KSeF}.xml: faktury sprzedaży przyjęte przez KSeF (XML wysłany do KSeF albo pobrany z KSeF).
            - sprzedaz/{numer KSeF}_UPO.xml: Urzędowe Poświadczenie Odbioru (UPO), jeśli jest zapisane w OpenKSeF.
            - zakup/{numer KSeF}.xml: faktury zakupu pobrane z KSeF.
            - indeks.csv: lista faktur z okresu (UTF-8, separator średnik). Faktura, której XML nie został jeszcze pobrany z KSeF, ma pustą kolumnę plik_xml.

            Plik XML jest fakturą (art. 106na ustawy o VAT). Wizualizację PDF można utworzyć z pliku XML w OpenKSeF albo w Aplikacji Podatnika KSeF Ministerstwa Finansów.
            Faktury przyjęte przez KSeF są przechowywane w KSeF przez 10 lat (art. 112aa ustawy o VAT).

            """;
    }

    private static string IssuedBuyerId(BuyerKind kind, string? idType, string? nip, string? country, string? foreignId) => (kind, idType) switch
    {
        (BuyerKind.Company, _) => nip ?? "",
        (BuyerKind.Foreign, IssuedInvoice.BuyerIdVatUe) => EuCountries.VatPrefix(country!) + foreignId,
        (BuyerKind.Foreign, IssuedInvoice.BuyerIdOther) => $"{country} {foreignId}",
        _ => "",
    };

    private static string? HeaderType(string? type) =>
        Enum.TryParse<IssuedInvoiceType>(type, true, out var known) ? known.Code() : type?.ToUpperInvariant();

    private static string Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
        return value.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}

public sealed record InvoiceExportRequest(Guid TenantId, DateOnly? From, DateOnly? To, bool Sales, bool Purchases);

public sealed record InvoiceExportRow(
    Guid Id, bool FromIssuing, string Direction, string KsefNumber, string? Number, string? Type, DateOnly IssueDate, DateOnly? AcceptedOn,
    string? SellerNip, string? SellerName, string? BuyerId, string? BuyerName, string Currency, decimal Net, decimal Vat, decimal Gross,
    DateOnly? DueDate, bool? Paid)
{
    public string? XmlFile { get; set; }
    public string? UpoFile { get; set; }
}

public sealed record InvoiceExport(InvoiceExportRequest Request, string Nip, string FileName, IReadOnlyList<InvoiceExportRow> Rows)
{
    public string AuditTarget =>
        $"od={Request.From:yyyy-MM-dd};do={Request.To:yyyy-MM-dd};kierunki={(Request.Sales ? "sprzedaz" : "")}{(Request.Sales && Request.Purchases ? "," : "")}{(Request.Purchases ? "zakup" : "")};" +
        $"sprzedaz={Rows.Count(r => r.Direction == "sprzedaz")};zakup={Rows.Count(r => r.Direction == "zakup")}";
}
