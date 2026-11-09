using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Platform.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "platform",
                table: "Permissions",
                columns: new[] { "Code", "Description" },
                values: new object[,]
                {
                    { "notifications.read", "Read notifications in the tenant outbox." },
                    { "notifications.send", "Write notifications to the tenant outbox." }
                });

            migrationBuilder.Sql(
                """
                INSERT INTO [platform].[TenantRolePermissions] ([TenantId], [RoleId], [PermissionCode])
                SELECT [TenantId], [RoleId], N'notifications.read'
                FROM [platform].[TenantRolePermissions]
                WHERE [PermissionCode] = N'roles.manage';

                INSERT INTO [platform].[TenantRolePermissions] ([TenantId], [RoleId], [PermissionCode])
                SELECT [TenantId], [RoleId], N'notifications.send'
                FROM [platform].[TenantRolePermissions]
                WHERE [PermissionCode] = N'roles.manage';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM [platform].[TenantRolePermissions]
                WHERE [PermissionCode] IN (N'notifications.read', N'notifications.send');
                """);

            migrationBuilder.DeleteData(
                schema: "platform",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "notifications.read");

            migrationBuilder.DeleteData(
                schema: "platform",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "notifications.send");
        }
    }
}
