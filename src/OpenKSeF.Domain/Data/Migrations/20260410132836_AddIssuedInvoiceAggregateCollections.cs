using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuedInvoiceAggregateCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdvanceDocumentIdsJson",
                table: "IssuedInvoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DuplicateIssuancesJson",
                table: "IssuedInvoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettledAdvanceAllocationsJson",
                table: "IssuedInvoices",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdvanceDocumentIdsJson",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "DuplicateIssuancesJson",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "SettledAdvanceAllocationsJson",
                table: "IssuedInvoices");
        }
    }
}
