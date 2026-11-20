using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <summary>ADR-004 M4 release 1: IssuedInvoices.SentXmlSha256 leaves the model, the column stays until release 2 drops it.</summary>
    public partial class UnmapIssuedInvoiceSentXmlSha256 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
