using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class NonUniqueTenantUserNipIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tenants_UserId_Nip",
                table: "Tenants");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_UserId_Nip",
                table: "Tenants",
                columns: new[] { "UserId", "Nip" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tenants_UserId_Nip",
                table: "Tenants");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_UserId_Nip",
                table: "Tenants",
                columns: new[] { "UserId", "Nip" },
                unique: true);
        }
    }
}
