using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoicePaymentFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FactorBankAccount",
                table: "InvoiceHeaders",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FactorName",
                table: "InvoiceHeaders",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FactorNip",
                table: "InvoiceHeaders",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "PartialPayment",
                table: "InvoiceHeaders",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SellerPaidOn",
                table: "InvoiceHeaders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Settlement",
                table: "InvoiceHeaders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SettlementAmount",
                table: "InvoiceHeaders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SplitPayment",
                table: "InvoiceHeaders",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FactorBankAccount",
                table: "InvoiceHeaders");

            migrationBuilder.DropColumn(
                name: "FactorName",
                table: "InvoiceHeaders");

            migrationBuilder.DropColumn(
                name: "FactorNip",
                table: "InvoiceHeaders");

            migrationBuilder.DropColumn(
                name: "PartialPayment",
                table: "InvoiceHeaders");

            migrationBuilder.DropColumn(
                name: "SellerPaidOn",
                table: "InvoiceHeaders");

            migrationBuilder.DropColumn(
                name: "Settlement",
                table: "InvoiceHeaders");

            migrationBuilder.DropColumn(
                name: "SettlementAmount",
                table: "InvoiceHeaders");

            migrationBuilder.DropColumn(
                name: "SplitPayment",
                table: "InvoiceHeaders");
        }
    }
}
