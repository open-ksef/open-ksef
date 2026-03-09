using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantNotificationEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotificationEmail",
                table: "Tenants",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotificationEmail",
                table: "Tenants");
        }
    }
}
