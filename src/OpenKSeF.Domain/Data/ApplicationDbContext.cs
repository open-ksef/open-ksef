using Microsoft.EntityFrameworkCore;
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
    public DbSet<InvoiceHeader> InvoiceHeaders => Set<InvoiceHeader>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();

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
    }
}
