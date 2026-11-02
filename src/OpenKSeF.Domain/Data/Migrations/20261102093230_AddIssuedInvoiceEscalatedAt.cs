using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuedInvoiceEscalatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EscalatedAt",
                table: "IssuedInvoices",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EscalatedAt",
                table: "IssuedInvoices");
        }
    }
}
