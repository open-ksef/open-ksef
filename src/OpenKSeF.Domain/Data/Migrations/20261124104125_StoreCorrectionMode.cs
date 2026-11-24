using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class StoreCorrectionMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrectionMode",
                table: "IssuedInvoices",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "IssuedInvoices"
                SET "CorrectionMode" = 'WrongBuyerId'
                WHERE "CorrectionReason" = 'Błędny NIP nabywcy'
                  AND "InvoiceType" IN ('KOR', 'KOR_ZAL', 'KOR_ROZ');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrectionMode",
                table: "IssuedInvoices");
        }
    }
}
