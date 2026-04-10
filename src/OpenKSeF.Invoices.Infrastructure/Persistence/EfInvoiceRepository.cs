using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Persistence;
using OpenKSeF.Invoices.Domain.ValueObjects;

namespace OpenKSeF.Invoices.Infrastructure.Persistence;

public sealed class EfInvoiceRepository(
    ApplicationDbContext db,
    IssuedInvoiceAggregateMapper mapper) : IInvoiceRepository
{
    public async Task<Invoice?> FindByIdAsync(InvoiceId id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        var record = await db.IssuedInvoices
            .AsNoTracking()
            .Include(invoice => invoice.Lines)
            .FirstOrDefaultAsync(invoice => invoice.Id == id.Value, ct);

        return record is null ? null : mapper.ToAggregate(record);
    }

    public async Task SaveAsync(Invoice invoice, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var record = await db.IssuedInvoices
            .Include(existing => existing.Lines)
            .FirstOrDefaultAsync(existing => existing.Id == invoice.Id.Value, ct);

        if (record is null)
        {
            record = new IssuedInvoiceRecord
            {
                Id = invoice.Id.Value,
                CreatedAt = DateTime.UtcNow
            };
            db.IssuedInvoices.Add(record);
        }

        mapper.Apply(invoice, record);
        record.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Invoice>> ListByTenantAsync(TenantId tenantId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var records = await db.IssuedInvoices
            .AsNoTracking()
            .Include(invoice => invoice.Lines)
            .Where(invoice => invoice.TenantId == tenantId.Value)
            .OrderByDescending(invoice => invoice.IssueDate)
            .ThenByDescending(invoice => invoice.DocumentNumber)
            .ToListAsync(ct);

        return records
            .Select(mapper.ToAggregate)
            .ToList();
    }
}
