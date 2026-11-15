using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class RebackfillTenantMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "InvoiceHeaders" SET "PaidByUserId" = NULL
                WHERE NOT "IsPaid" AND "PaidByUserId" IS NOT NULL;
                """);

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
        }
    }
}
