using Microsoft.EntityFrameworkCore;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Invoicing;

namespace OpenKSeF.Domain.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<KSeFCredential> KSeFCredentials => Set<KSeFCredential>();
    public DbSet<InvoiceHeader> InvoiceHeaders => Set<InvoiceHeader>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();
    public DbSet<IssuedInvoice> IssuedInvoices => Set<IssuedInvoice>();
    public DbSet<IssuedInvoiceLine> IssuedInvoiceLines => Set<IssuedInvoiceLine>();
    public DbSet<SellerProfile> SellerProfiles => Set<SellerProfile>();
    public DbSet<InvoiceNumberCounter> InvoiceNumberCounters => Set<InvoiceNumberCounter>();

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

        // InvoiceHeader
        modelBuilder.Entity<InvoiceHeader>(entity =>
        {
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

        // InvoiceLine
        modelBuilder.Entity<InvoiceLine>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.HasIndex(l => l.InvoiceHeaderId);
            entity.Property(l => l.Name).HasMaxLength(512);
            entity.Property(l => l.Unit).HasMaxLength(50);
            entity.Property(l => l.VatRate).HasMaxLength(50);
            entity.Property(l => l.Quantity).HasPrecision(18, 6);
            entity.Property(l => l.UnitPriceNet).HasPrecision(18, 6);
            entity.Property(l => l.UnitPriceGross).HasPrecision(18, 6);
            entity.Property(l => l.AmountNet).HasPrecision(18, 2);
            entity.Property(l => l.AmountGross).HasPrecision(18, 2);
            entity.Property(l => l.AmountVat).HasPrecision(18, 2);
            entity.HasOne(l => l.InvoiceHeader)
                .WithMany(i => i.Lines)
                .HasForeignKey(l => l.InvoiceHeaderId)
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

        modelBuilder.Entity<IssuedInvoice>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property<uint>("Version").IsRowVersion();
            entity.HasIndex(i => new { i.TenantId, i.Number }).IsUnique().HasFilter("\"Number\" IS NOT NULL");
            entity.HasIndex(i => new { i.Status, i.NextAttemptAt });
            entity.HasIndex(i => new { i.TenantId, i.CreatedAt });
            entity.Property(i => i.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(i => i.Number).HasMaxLength(256);
            entity.Property(i => i.SellerNip).HasMaxLength(10);
            entity.Property(i => i.SellerName).HasMaxLength(512);
            entity.Property(i => i.SellerAddressLine1).HasMaxLength(512);
            entity.Property(i => i.SellerAddressLine2).HasMaxLength(512);
            entity.Property(i => i.SellerBankAccount).HasMaxLength(26);
            entity.Property(i => i.BuyerKind).HasConversion<string>().HasMaxLength(16);
            entity.Property(i => i.BuyerNip).HasMaxLength(10);
            entity.Property(i => i.BuyerName).HasMaxLength(512);
            entity.Property(i => i.BuyerAddressLine1).HasMaxLength(512);
            entity.Property(i => i.BuyerAddressLine2).HasMaxLength(512);
            entity.Property(i => i.PaymentMethod).HasConversion<string>().HasMaxLength(16);
            entity.Property(i => i.ExemptionBasisKind).HasConversion<string>().HasMaxLength(16);
            entity.Property(i => i.ExemptionBasisText).HasMaxLength(256);
            entity.Property(i => i.TotalNet).HasPrecision(18, 2);
            entity.Property(i => i.TotalVat).HasPrecision(18, 2);
            entity.Property(i => i.TotalGross).HasPrecision(18, 2);
            entity.Property(i => i.KsefEnvironment).HasMaxLength(16);
            entity.Property(i => i.SentXmlSha256).HasMaxLength(64);
            entity.Property(i => i.VerificationUrl).HasMaxLength(512);
            entity.Property(i => i.KsefSessionRef).HasMaxLength(100);
            entity.Property(i => i.KsefElementRef).HasMaxLength(100);
            entity.Property(i => i.KsefNumber).HasMaxLength(64);
            entity.Property(i => i.KsefStatusDescription).HasMaxLength(1000);
            entity.Property(i => i.LastError).HasMaxLength(1000);
            entity.HasMany(i => i.Lines)
                .WithOne()
                .HasForeignKey(l => l.IssuedInvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(i => i.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IssuedInvoiceLine>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.Property(l => l.Id).ValueGeneratedNever();
            entity.HasIndex(l => new { l.IssuedInvoiceId, l.LineNo });
            entity.Property(l => l.Name).IsRequired().HasMaxLength(512);
            entity.Property(l => l.Unit).IsRequired().HasMaxLength(256);
            entity.Property(l => l.Quantity).HasPrecision(18, 6);
            entity.Property(l => l.UnitNetPrice).HasPrecision(18, 2);
            entity.Property(l => l.VatRate)
                .HasConversion(r => r.Code(), code => VatRates.Parse(code))
                .HasMaxLength(8);
            entity.Property(l => l.NetAmount).HasPrecision(18, 2);
            entity.Property(l => l.VatAmount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<SellerProfile>(entity =>
        {
            entity.HasKey(p => p.TenantId);
            entity.Property<uint>("Version").IsRowVersion();
            entity.Property(p => p.LegalName).HasMaxLength(512);
            entity.Property(p => p.AddressLine1).HasMaxLength(512);
            entity.Property(p => p.AddressLine2).HasMaxLength(512);
            entity.Property(p => p.BankAccount).HasMaxLength(26);
            entity.Property(p => p.NumberingPattern).IsRequired().HasMaxLength(100);
            entity.HasOne<Tenant>()
                .WithOne()
                .HasForeignKey<SellerProfile>(p => p.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvoiceNumberCounter>(entity =>
        {
            entity.HasKey(c => new { c.TenantId, c.Period });
            entity.Property(c => c.Period).HasMaxLength(7);
            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(c => c.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
