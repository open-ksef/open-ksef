using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderChains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdvanceNumberingPattern",
                table: "SellerProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "ZAL/{nr}/{MM}/{RRRR}");

            migrationBuilder.AddColumn<decimal>(
                name: "AdvanceAmount",
                table: "IssuedInvoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrderChainId",
                table: "IssuedInvoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssuedInvoices_TenantId_OrderChainId",
                table: "IssuedInvoices",
                columns: new[] { "TenantId", "OrderChainId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IssuedInvoices_TenantId_OrderChainId",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "AdvanceNumberingPattern",
                table: "SellerProfiles");

            migrationBuilder.DropColumn(
                name: "AdvanceAmount",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "OrderChainId",
                table: "IssuedInvoices");
        }
    }
}
