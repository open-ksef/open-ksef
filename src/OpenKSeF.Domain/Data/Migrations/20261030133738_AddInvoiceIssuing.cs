using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceIssuing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceNumberCounters",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Next = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceNumberCounters", x => new { x.TenantId, x.Period });
                    table.ForeignKey(
                        name: "FK_InvoiceNumberCounters_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssuedInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Number = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SellerNip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    SellerName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SellerAddressLine1 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SellerAddressLine2 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SellerBankAccount = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    SellerCashAccounting = table.Column<bool>(type: "boolean", nullable: false),
                    BuyerKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BuyerNip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    BuyerName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    BuyerAddressLine1 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    BuyerAddressLine2 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SaleDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SplitPayment = table.Column<bool>(type: "boolean", nullable: false),
                    ExemptionBasisKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ExemptionBasisText = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TotalNet = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalVat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalGross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    KsefEnvironment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    SentXml = table.Column<byte[]>(type: "bytea", nullable: true),
                    SentXmlSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    VerificationUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    KsefSessionRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    KsefElementRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SendStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    KsefNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    KsefStatusCode = table.Column<int>(type: "integer", nullable: true),
                    KsefStatusDescription = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    KsefStatusDetails = table.Column<string>(type: "text", nullable: true),
                    UpoXml = table.Column<byte[]>(type: "bytea", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuedInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssuedInvoices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SellerProfiles",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AddressLine1 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AddressLine2 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    BankAccount = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    CashAccounting = table.Column<bool>(type: "boolean", nullable: false),
                    NumberingPattern = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerProfiles", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_SellerProfiles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssuedInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNo = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Unit = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitNetPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatRate = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuedInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssuedInvoiceLines_IssuedInvoices_IssuedInvoiceId",
                        column: x => x.IssuedInvoiceId,
                        principalTable: "IssuedInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssuedInvoiceLines_IssuedInvoiceId_LineNo",
                table: "IssuedInvoiceLines",
                columns: new[] { "IssuedInvoiceId", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "IX_IssuedInvoices_Status_NextAttemptAt",
                table: "IssuedInvoices",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IssuedInvoices_TenantId_CreatedAt",
                table: "IssuedInvoices",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IssuedInvoices_TenantId_Number",
                table: "IssuedInvoices",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "\"Number\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceNumberCounters");

            migrationBuilder.DropTable(
                name: "IssuedInvoiceLines");

            migrationBuilder.DropTable(
                name: "SellerProfiles");

            migrationBuilder.DropTable(
                name: "IssuedInvoices");
        }
    }
}
