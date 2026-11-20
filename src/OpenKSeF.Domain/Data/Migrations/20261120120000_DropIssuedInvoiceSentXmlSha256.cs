using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <summary>ADR-004 M4 release 2: drops IssuedInvoices.SentXmlSha256, unmapped since 20261120090000 (v0.0.32). Hand-written: the snapshot no longer has the column, so EF cannot generate it.</summary>
    public partial class DropIssuedInvoiceSentXmlSha256 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SentXmlSha256",
                table: "IssuedInvoices");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SentXmlSha256",
                table: "IssuedInvoices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }
    }
}
