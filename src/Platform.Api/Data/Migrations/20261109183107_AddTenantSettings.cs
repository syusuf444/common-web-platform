using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Platform.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantSettings",
                schema: "platform",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSettings", x => new { x.TenantId, x.Key });
                    table.ForeignKey(
                        name: "FK_TenantSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "platform",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "platform",
                table: "Permissions",
                columns: new[] { "Code", "Description" },
                values: new object[,]
                {
                    { "settings.manage", "Create, update, and delete non-secret tenant settings." },
                    { "settings.read", "Read non-secret tenant settings." }
                });

            migrationBuilder.Sql(
                """
                INSERT INTO [platform].[TenantRolePermissions] ([TenantId], [RoleId], [PermissionCode])
                SELECT [TenantId], [RoleId], N'settings.manage'
                FROM [platform].[TenantRolePermissions]
                WHERE [PermissionCode] = N'roles.manage';

                INSERT INTO [platform].[TenantRolePermissions] ([TenantId], [RoleId], [PermissionCode])
                SELECT [TenantId], [RoleId], N'settings.read'
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
                WHERE [PermissionCode] IN (N'settings.manage', N'settings.read');
                """);

            migrationBuilder.DropTable(
                name: "TenantSettings",
                schema: "platform");

            migrationBuilder.DeleteData(
                schema: "platform",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "settings.manage");

            migrationBuilder.DeleteData(
                schema: "platform",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "settings.read");
        }
    }
}
