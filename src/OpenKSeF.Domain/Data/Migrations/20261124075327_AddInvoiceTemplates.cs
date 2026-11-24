using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BuyerKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BuyerNip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    BuyerName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    BuyerAddressLine1 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    BuyerAddressLine2 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    BuyerCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    BuyerIdType = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    BuyerForeignId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PaymentDays = table.Column<int>(type: "integer", nullable: false),
                    SplitPayment = table.Column<bool>(type: "boolean", nullable: false),
                    ExemptionBasisKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ExemptionBasisText = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceTemplates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceTemplateLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNo = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Unit = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitNetPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatRate = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceTemplateLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceTemplateLines_InvoiceTemplates_InvoiceTemplateId",
                        column: x => x.InvoiceTemplateId,
                        principalTable: "InvoiceTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceTemplateLines_InvoiceTemplateId_LineNo",
                table: "InvoiceTemplateLines",
                columns: new[] { "InvoiceTemplateId", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceTemplates_TenantId",
                table: "InvoiceTemplates",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceTemplateLines");

            migrationBuilder.DropTable(
                name: "InvoiceTemplates");
        }
    }
}
