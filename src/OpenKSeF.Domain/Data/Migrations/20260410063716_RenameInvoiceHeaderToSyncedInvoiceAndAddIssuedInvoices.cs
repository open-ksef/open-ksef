using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameInvoiceHeaderToSyncedInvoiceAndAddIssuedInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceHeaders_Tenants_TenantId",
                table: "InvoiceHeaders");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceLines_InvoiceHeaders_InvoiceHeaderId",
                table: "InvoiceLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_InvoiceLines",
                table: "InvoiceLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_InvoiceHeaders",
                table: "InvoiceHeaders");

            migrationBuilder.RenameTable(
                name: "InvoiceLines",
                newName: "SyncedInvoiceLines");

            migrationBuilder.RenameTable(
                name: "InvoiceHeaders",
                newName: "SyncedInvoices");

            // Recreate indexes with new names (RenameIndex not supported on all providers e.g. SQLite)
            migrationBuilder.DropIndex(
                name: "IX_InvoiceLines_InvoiceHeaderId",
                table: "SyncedInvoiceLines");
            migrationBuilder.CreateIndex(
                name: "IX_SyncedInvoiceLines_InvoiceHeaderId",
                table: "SyncedInvoiceLines",
                column: "InvoiceHeaderId");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeaders_TenantId_KSeFInvoiceNumber",
                table: "SyncedInvoices");
            migrationBuilder.CreateIndex(
                name: "IX_SyncedInvoices_TenantId_KSeFInvoiceNumber",
                table: "SyncedInvoices",
                columns: new[] { "TenantId", "KSeFInvoiceNumber" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeaders_IssueDate",
                table: "SyncedInvoices");
            migrationBuilder.CreateIndex(
                name: "IX_SyncedInvoices_IssueDate",
                table: "SyncedInvoices",
                column: "IssueDate");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SyncedInvoiceLines",
                table: "SyncedInvoiceLines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SyncedInvoices",
                table: "SyncedInvoices",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "IssuedInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BuyerKind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    KsefSubmissionRequirement = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    KsefSubmissionState = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SellerName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SellerNip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    BuyerName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BuyerNip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    IssueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SaleDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedToKsefAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedByKsefAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "PLN"),
                    TotalNet = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalVat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalGross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PaymentMethod = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PublicNotes = table.Column<string>(type: "text", nullable: true),
                    InternalNotes = table.Column<string>(type: "text", nullable: true),
                    KsefDocumentNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    KsefReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    KsefRejectionReason = table.Column<string>(type: "text", nullable: true),
                    CorrectionOriginalInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrectionOriginalDocumentNumber = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CorrectionReasonKind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CorrectionReasonDescription = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuedInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssuedInvoices_Tenants_TenantId",
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
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PricingMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    VatRate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    VatClassification = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CorrectionRole = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    NetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
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
                name: "IX_IssuedInvoiceLines_IssuedInvoiceId",
                table: "IssuedInvoiceLines",
                column: "IssuedInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_IssuedInvoices_TenantId",
                table: "IssuedInvoices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IssuedInvoices_TenantId_DocumentNumber",
                table: "IssuedInvoices",
                columns: new[] { "TenantId", "DocumentNumber" },
                unique: true,
                filter: "\"DocumentNumber\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SyncedInvoiceLines_SyncedInvoices_InvoiceHeaderId",
                table: "SyncedInvoiceLines",
                column: "InvoiceHeaderId",
                principalTable: "SyncedInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SyncedInvoices_Tenants_TenantId",
                table: "SyncedInvoices",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SyncedInvoiceLines_SyncedInvoices_InvoiceHeaderId",
                table: "SyncedInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SyncedInvoices_Tenants_TenantId",
                table: "SyncedInvoices");

            migrationBuilder.DropTable(
                name: "IssuedInvoiceLines");

            migrationBuilder.DropTable(
                name: "IssuedInvoices");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SyncedInvoices",
                table: "SyncedInvoices");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SyncedInvoiceLines",
                table: "SyncedInvoiceLines");

            migrationBuilder.RenameTable(
                name: "SyncedInvoices",
                newName: "InvoiceHeaders");

            migrationBuilder.RenameTable(
                name: "SyncedInvoiceLines",
                newName: "InvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_SyncedInvoices_TenantId_KSeFInvoiceNumber",
                table: "InvoiceHeaders");
            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeaders_TenantId_KSeFInvoiceNumber",
                table: "InvoiceHeaders",
                columns: new[] { "TenantId", "KSeFInvoiceNumber" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_SyncedInvoices_IssueDate",
                table: "InvoiceHeaders");
            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeaders_IssueDate",
                table: "InvoiceHeaders",
                column: "IssueDate");

            migrationBuilder.DropIndex(
                name: "IX_SyncedInvoiceLines_InvoiceHeaderId",
                table: "InvoiceLines");
            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceHeaderId",
                table: "InvoiceLines",
                column: "InvoiceHeaderId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_InvoiceHeaders",
                table: "InvoiceHeaders",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_InvoiceLines",
                table: "InvoiceLines",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceHeaders_Tenants_TenantId",
                table: "InvoiceHeaders",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceLines_InvoiceHeaders_InvoiceHeaderId",
                table: "InvoiceLines",
                column: "InvoiceHeaderId",
                principalTable: "InvoiceHeaders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
