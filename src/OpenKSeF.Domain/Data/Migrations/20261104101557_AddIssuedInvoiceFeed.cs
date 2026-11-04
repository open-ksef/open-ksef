using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuedInvoiceFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "IssuedContinuationAt",
                table: "SyncStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerForeignId",
                table: "InvoiceHeaders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerIdType",
                table: "InvoiceHeaders",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Direction",
                table: "InvoiceHeaders",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Received");

            migrationBuilder.CreateIndex(
                name: "IX_IssuedInvoices_TenantId_KsefNumber",
                table: "IssuedInvoices",
                columns: new[] { "TenantId", "KsefNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IssuedInvoices_TenantId_KsefNumber",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "IssuedContinuationAt",
                table: "SyncStates");

            migrationBuilder.DropColumn(
                name: "BuyerForeignId",
                table: "InvoiceHeaders");

            migrationBuilder.DropColumn(
                name: "BuyerIdType",
                table: "InvoiceHeaders");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "InvoiceHeaders");
        }
    }
}
