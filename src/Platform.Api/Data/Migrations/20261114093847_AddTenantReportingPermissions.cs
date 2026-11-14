using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Platform.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantReportingPermissions : Migration
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
                    { "reports.export", "Export tenant report data." },
                    { "reports.read", "Read tenant dashboard and report data." }
                });

            migrationBuilder.Sql(
                """
                INSERT INTO [platform].[TenantRolePermissions] ([TenantId], [RoleId], [PermissionCode])
                SELECT [TenantId], [RoleId], N'reports.read'
                FROM [platform].[TenantRolePermissions]
                WHERE [PermissionCode] = N'roles.manage';

                INSERT INTO [platform].[TenantRolePermissions] ([TenantId], [RoleId], [PermissionCode])
                SELECT [TenantId], [RoleId], N'reports.export'
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
                WHERE [PermissionCode] IN (N'reports.export', N'reports.read');
                """);

            migrationBuilder.DeleteData(
                schema: "platform",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "reports.export");

            migrationBuilder.DeleteData(
                schema: "platform",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "reports.read");
        }
    }
}
