using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExtendInvoiceHeaderFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "InvoiceHeaders",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerName",
                table: "InvoiceHeaders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerNip",
                table: "InvoiceHeaders",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountNet",
                table: "InvoiceHeaders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountVat",
                table: "InvoiceHeaders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcquisitionDate",
                table: "InvoiceHeaders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceType",
                table: "InvoiceHeaders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.DropColumn(
                name: "SaleDate",
                table: "InvoiceHeaders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SaleDate",
                table: "InvoiceHeaders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.DropColumn(name: "InvoiceNumber", table: "InvoiceHeaders");
            migrationBuilder.DropColumn(name: "BuyerName", table: "InvoiceHeaders");
            migrationBuilder.DropColumn(name: "BuyerNip", table: "InvoiceHeaders");
            migrationBuilder.DropColumn(name: "AmountNet", table: "InvoiceHeaders");
            migrationBuilder.DropColumn(name: "AmountVat", table: "InvoiceHeaders");
            migrationBuilder.DropColumn(name: "AcquisitionDate", table: "InvoiceHeaders");
            migrationBuilder.DropColumn(name: "InvoiceType", table: "InvoiceHeaders");
        }
    }
}
