using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlignSyncColumnsWithKsef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPriceNet",
                table: "InvoiceLines",
                type: "numeric(22,6)",
                precision: 22,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPriceGross",
                table: "InvoiceLines",
                type: "numeric(22,6)",
                precision: 22,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Unit",
                table: "InvoiceLines",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "InvoiceLines",
                type: "numeric(22,6)",
                precision: 22,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "VendorName",
                table: "InvoiceHeaders",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "BuyerName",
                table: "InvoiceHeaders",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPriceNet",
                table: "InvoiceLines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(22,6)",
                oldPrecision: 22,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPriceGross",
                table: "InvoiceLines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(22,6)",
                oldPrecision: 22,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Unit",
                table: "InvoiceLines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "InvoiceLines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(22,6)",
                oldPrecision: 22,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "VendorName",
                table: "InvoiceHeaders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AlterColumn<string>(
                name: "BuyerName",
                table: "InvoiceHeaders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);
        }
    }
}
