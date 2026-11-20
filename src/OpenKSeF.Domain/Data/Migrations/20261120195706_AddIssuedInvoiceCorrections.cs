using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuedInvoiceCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrectionNumberingPattern",
                table: "SellerProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "KOR/{nr}/{MM}/{RRRR}");

            migrationBuilder.AddColumn<Guid>(
                name: "CorrectedInvoiceId",
                table: "IssuedInvoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                table: "IssuedInvoices",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CorrectionType",
                table: "IssuedInvoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceType",
                table: "IssuedInvoices",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "VAT");

            migrationBuilder.AddColumn<int>(
                name: "BaseLineNo",
                table: "IssuedInvoiceLines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NumberSeriesCounters",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Series = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Next = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberSeriesCounters", x => new { x.TenantId, x.Series, x.Period });
                    table.ForeignKey(
                        name: "FK_NumberSeriesCounters_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssuedInvoices_TenantId_CorrectedInvoiceId",
                table: "IssuedInvoices",
                columns: new[] { "TenantId", "CorrectedInvoiceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NumberSeriesCounters");

            migrationBuilder.DropIndex(
                name: "IX_IssuedInvoices_TenantId_CorrectedInvoiceId",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "CorrectionNumberingPattern",
                table: "SellerProfiles");

            migrationBuilder.DropColumn(
                name: "CorrectedInvoiceId",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "CorrectionType",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "InvoiceType",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "BaseLineNo",
                table: "IssuedInvoiceLines");
        }
    }
}
