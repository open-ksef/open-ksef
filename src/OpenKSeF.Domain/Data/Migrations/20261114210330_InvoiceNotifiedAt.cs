using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceNotifiedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NotifiedAt",
                table: "InvoiceHeaders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""UPDATE "InvoiceHeaders" SET "NotifiedAt" = "FirstSeenAt" WHERE "NotifiedAt" IS NULL;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotifiedAt",
                table: "InvoiceHeaders");
        }
    }
}
