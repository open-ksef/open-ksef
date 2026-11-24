using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProformas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProformaNumberingPattern",
                table: "SellerProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "PRO/{nr}/{MM}/{RRRR}");

            migrationBuilder.AddColumn<Guid>(
                name: "ProformaId",
                table: "IssuedInvoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssuedInvoices_TenantId_ProformaId",
                table: "IssuedInvoices",
                columns: new[] { "TenantId", "ProformaId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IssuedInvoices_TenantId_ProformaId",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "ProformaNumberingPattern",
                table: "SellerProfiles");

            migrationBuilder.DropColumn(
                name: "ProformaId",
                table: "IssuedInvoices");
        }
    }
}
