using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaidByUserId",
                table: "InvoiceHeaders",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TenantMembers",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantMembers", x => new { x.TenantId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TenantMembers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantMembers_UserId",
                table: "TenantMembers",
                column: "UserId");

            migrationBuilder.Sql("""
                INSERT INTO "TenantMembers" ("TenantId", "UserId", "Role", "CreatedAt")
                SELECT t."Id", t."UserId", 'Owner', t."CreatedAt" FROM "Tenants" t
                WHERE NOT EXISTS (SELECT 1 FROM "TenantMembers" m WHERE m."TenantId" = t."Id" AND m."UserId" = t."UserId");
                """);

            migrationBuilder.Sql("""
                UPDATE "InvoiceHeaders"
                SET "PaidByUserId" = (SELECT t."UserId" FROM "Tenants" t WHERE t."Id" = "InvoiceHeaders"."TenantId")
                WHERE "IsPaid" AND "PaidByUserId" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantMembers");

            migrationBuilder.DropColumn(
                name: "PaidByUserId",
                table: "InvoiceHeaders");
        }
    }
}
