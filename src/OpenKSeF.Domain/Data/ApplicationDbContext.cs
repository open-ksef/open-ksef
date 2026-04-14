using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenKSeF.Domain.Entities;

namespace OpenKSeF.Domain.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<KSeFCredential> KSeFCredentials => Set<KSeFCredential>();
    public DbSet<SyncedInvoice> SyncedInvoices => Set<SyncedInvoice>();
    public DbSet<SyncedInvoiceLine> SyncedInvoiceLines => Set<SyncedInvoiceLine>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();

    // Issuing domain — new Invoice aggregate persistence
    public DbSet<IssuedInvoiceRecord> IssuedInvoices => Set<IssuedInvoiceRecord>();
    public DbSet<IssuedInvoiceLineRecord> IssuedInvoiceLines => Set<IssuedInvoiceLineRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tenant
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => new { t.UserId, t.Nip }).IsUnique();
            entity.Property(t => t.UserId).IsRequired().HasMaxLength(256);
            entity.Property(t => t.Nip).IsRequired().HasMaxLength(10);
            entity.Property(t => t.DisplayName).HasMaxLength(200);
        });

        // KSeFCredential
        modelBuilder.Entity<KSeFCredential>(entity =>
        {
            entity.HasKey(k => k.Id);
            entity.Property(k => k.Type)
                .HasDefaultValue(CredentialType.Token)
                .HasConversion<int>();
            entity.Property(k => k.CertificateFingerprint).HasMaxLength(128);
            entity.HasOne(k => k.Tenant)
                .WithMany(t => t.KSeFCredentials)
                .HasForeignKey(k => k.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // SyncedInvoice (synced read model; table: SyncedInvoices)
        modelBuilder.Entity<SyncedInvoice>(entity =>
        {
            entity.ToTable("SyncedInvoices");
            entity.HasKey(i => i.Id);
            entity.HasIndex(i => new { i.TenantId, i.KSeFInvoiceNumber }).IsUnique();
            entity.HasIndex(i => i.IssueDate);
            entity.Property(i => i.KSeFInvoiceNumber).IsRequired().HasMaxLength(100);
            entity.Property(i => i.KSeFReferenceNumber).IsRequired().HasMaxLength(100);
            entity.Property(i => i.InvoiceNumber).HasMaxLength(256);
            entity.Property(i => i.VendorName).IsRequired().HasMaxLength(500);
            entity.Property(i => i.VendorNip).IsRequired().HasMaxLength(10);
            entity.Property(i => i.BuyerName).HasMaxLength(500);
            entity.Property(i => i.BuyerNip).HasMaxLength(10);
            entity.Property(i => i.AmountNet).HasPrecision(18, 2);
            entity.Property(i => i.AmountVat).HasPrecision(18, 2);
            entity.Property(i => i.AmountGross).HasPrecision(18, 2);
            entity.Property(i => i.Currency).IsRequired().HasMaxLength(3).HasDefaultValue("PLN");
            entity.Property(i => i.InvoiceType).HasMaxLength(50);
            entity.Property(i => i.VendorBankAccount).HasMaxLength(34);
            entity.HasOne(i => i.Tenant)
                .WithMany(t => t.Invoices)
                .HasForeignKey(i => i.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // SyncedInvoiceLine (synced read model; table: SyncedInvoiceLines)
        modelBuilder.Entity<SyncedInvoiceLine>(entity =>
        {
            entity.ToTable("SyncedInvoiceLines");
            entity.HasKey(l => l.Id);
            // Column is still named InvoiceHeaderId in the database (legacy name preserved)
            entity.Property(l => l.SyncedInvoiceId).HasColumnName("InvoiceHeaderId");
            entity.HasIndex(l => l.SyncedInvoiceId).HasDatabaseName("IX_SyncedInvoiceLines_InvoiceHeaderId");
            entity.Property(l => l.Name).HasMaxLength(512);
            entity.Property(l => l.Unit).HasMaxLength(50);
            entity.Property(l => l.VatRate).HasMaxLength(50);
            entity.Property(l => l.Quantity).HasPrecision(18, 6);
            entity.Property(l => l.UnitPriceNet).HasPrecision(18, 6);
            entity.Property(l => l.UnitPriceGross).HasPrecision(18, 6);
            entity.Property(l => l.AmountNet).HasPrecision(18, 2);
            entity.Property(l => l.AmountGross).HasPrecision(18, 2);
            entity.Property(l => l.AmountVat).HasPrecision(18, 2);
            entity.HasOne(l => l.SyncedInvoice)
                .WithMany(i => i.Lines)
                .HasForeignKey(l => l.SyncedInvoiceId)
                .HasConstraintName("FK_SyncedInvoiceLines_SyncedInvoices_InvoiceHeaderId")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // SyncState (one-to-one with Tenant)
        modelBuilder.Entity<SyncState>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.TenantId).IsUnique();
            entity.HasOne(s => s.Tenant)
                .WithOne(t => t.SyncState)
                .HasForeignKey<SyncState>(s => s.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // DeviceToken
        modelBuilder.Entity<DeviceToken>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.HasIndex(d => d.UserId);
            entity.Property(d => d.UserId).IsRequired().HasMaxLength(256);
            entity.Property(d => d.Token).IsRequired().HasMaxLength(512);
            entity.HasOne(d => d.Tenant)
                .WithMany(t => t.DeviceTokens)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // SystemConfig (key-value store for runtime config)
        modelBuilder.Entity<SystemConfig>(entity =>
        {
            entity.HasKey(s => s.Key);
            entity.Property(s => s.Key).HasMaxLength(128);
            entity.Property(s => s.Value).IsRequired();
        });

        // IssuedInvoiceRecord — new Invoice aggregate (issuing domain)
        modelBuilder.Entity<IssuedInvoiceRecord>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.ToTable("IssuedInvoices");
            entity.HasIndex(i => i.TenantId);
            entity.HasIndex(i => new { i.TenantId, i.DocumentNumber }).IsUnique().HasFilter("\"DocumentNumber\" IS NOT NULL");
            entity.Property(i => i.Kind).IsRequired().HasMaxLength(50);
            entity.Property(i => i.Status).IsRequired().HasMaxLength(50);
            entity.Property(i => i.BuyerKind).IsRequired().HasMaxLength(50);
            entity.Property(i => i.KsefSubmissionRequirement).IsRequired().HasMaxLength(50);
            entity.Property(i => i.KsefSubmissionState).IsRequired().HasMaxLength(50);
            entity.Property(i => i.SellerName).IsRequired().HasMaxLength(500);
            entity.Property(i => i.SellerNip).IsRequired().HasMaxLength(10);
            entity.Property(i => i.BuyerName).IsRequired().HasMaxLength(500);
            entity.Property(i => i.BuyerNip).HasMaxLength(10);
            entity.Property(i => i.Currency).IsRequired().HasMaxLength(3).HasDefaultValue("PLN");
            entity.Property(i => i.TotalNet).HasPrecision(18, 2);
            entity.Property(i => i.TotalVat).HasPrecision(18, 2);
            entity.Property(i => i.TotalGross).HasPrecision(18, 2);
            entity.Property(i => i.DocumentNumber).HasMaxLength(256);
            entity.Property(i => i.ExternalReference).HasMaxLength(256);
            entity.Property(i => i.PaymentMethod).HasMaxLength(100);
            entity.Property(i => i.KsefDocumentNumber).HasMaxLength(100);
            entity.Property(i => i.KsefReferenceNumber).HasMaxLength(100);
            entity.Property(i => i.AdvanceDocumentIdsJson);
            entity.Property(i => i.SettledAdvanceAllocationsJson);
            entity.Property(i => i.DuplicateIssuancesJson);
            entity.Property(i => i.CorrectionOriginalDocumentNumber).HasMaxLength(256);
            entity.Property(i => i.CorrectionReasonKind).HasMaxLength(50);
            entity.HasOne(i => i.Tenant)
                .WithMany()
                .HasForeignKey(i => i.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // IssuedInvoiceLineRecord
        modelBuilder.Entity<IssuedInvoiceLineRecord>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.ToTable("IssuedInvoiceLines");
            entity.HasIndex(l => l.IssuedInvoiceId);
            entity.Property(l => l.Description).IsRequired().HasMaxLength(512);
            entity.Property(l => l.UnitOfMeasure).HasMaxLength(50);
            entity.Property(l => l.PricingMode).IsRequired().HasMaxLength(20);
            entity.Property(l => l.VatRate).IsRequired().HasMaxLength(50);
            entity.Property(l => l.VatClassification).HasMaxLength(100);
            entity.Property(l => l.CorrectionRole).HasMaxLength(30);
            entity.Property(l => l.Quantity).HasPrecision(18, 6);
            entity.Property(l => l.UnitPrice).HasPrecision(18, 6);
            entity.Property(l => l.DiscountPercent).HasPrecision(5, 2);
            entity.Property(l => l.NetAmount).HasPrecision(18, 2);
            entity.Property(l => l.VatAmount).HasPrecision(18, 2);
            entity.Property(l => l.GrossAmount).HasPrecision(18, 2);
            entity.HasOne(l => l.IssuedInvoice)
                .WithMany(i => i.Lines)
                .HasForeignKey(l => l.IssuedInvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Normalize all DateTime/DateTime? properties to UTC before persisting.
        // Npgsql requires DateTimeKind.Utc for 'timestamp with time zone' columns.
        // IssueDate and other date fields come from JSON with DateTimeKind.Unspecified.
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        var utcNullableConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue
                ? (v.Value.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc))
                : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                    property.SetValueConverter(utcConverter);
                else if (property.ClrType == typeof(DateTime?))
                    property.SetValueConverter(utcNullableConverter);
            }
        }
    }
}
