using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceXmlFetchedMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedContinuationAt",
                table: "SyncStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "XmlAttemptedAt",
                table: "InvoiceHeaders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "XmlFetchedAt",
                table: "InvoiceHeaders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceivedContinuationAt",
                table: "SyncStates");

            migrationBuilder.DropColumn(
                name: "XmlAttemptedAt",
                table: "InvoiceHeaders");

            migrationBuilder.DropColumn(
                name: "XmlFetchedAt",
                table: "InvoiceHeaders");
        }
    }
}
