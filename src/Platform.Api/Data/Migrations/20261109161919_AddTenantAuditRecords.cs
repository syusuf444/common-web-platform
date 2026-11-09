using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantAuditRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditRecords",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ResourceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRecords", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "platform",
                table: "Permissions",
                columns: new[] { "Code", "Description" },
                values: new object[] { "audit.read", "Read tenant security and administration audit records." });

            migrationBuilder.Sql(
                """
                INSERT INTO [platform].[TenantRolePermissions] ([TenantId], [RoleId], [PermissionCode])
                SELECT [TenantId], [RoleId], N'audit.read'
                FROM [platform].[TenantRolePermissions]
                WHERE [PermissionCode] = N'roles.manage';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_ActorUserId",
                schema: "platform",
                table: "AuditRecords",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_CorrelationId",
                schema: "platform",
                table: "AuditRecords",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_TenantId_OccurredAtUtc",
                schema: "platform",
                table: "AuditRecords",
                columns: new[] { "TenantId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM [platform].[TenantRolePermissions]
                WHERE [PermissionCode] = N'audit.read';
                """);

            migrationBuilder.DropTable(
                name: "AuditRecords",
                schema: "platform");

            migrationBuilder.DeleteData(
                schema: "platform",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "audit.read");
        }
    }
}
