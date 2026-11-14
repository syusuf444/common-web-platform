using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleAccessRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoleAccessRequests",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequesterMembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequesterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleAccessRequests", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_RoleAccessRequests_TenantRoles_TenantId_RoleId",
                        columns: x => new { x.TenantId, x.RoleId },
                        principalSchema: "platform",
                        principalTable: "TenantRoles",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "platform",
                table: "Permissions",
                columns: new[] { "Code", "Description" },
                values: new object[] { "workflows.manage", "Review and decide tenant role access requests." });

            migrationBuilder.Sql(
                """
                INSERT INTO [platform].[TenantRolePermissions] ([TenantId], [RoleId], [PermissionCode])
                SELECT [TenantId], [RoleId], N'workflows.manage'
                FROM [platform].[TenantRolePermissions]
                WHERE [PermissionCode] = N'roles.manage';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RoleAccessRequests_TenantId_RequesterMembershipId_RoleId",
                schema: "platform",
                table: "RoleAccessRequests",
                columns: new[] { "TenantId", "RequesterMembershipId", "RoleId" },
                unique: true,
                filter: "[Status] = N'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_RoleAccessRequests_TenantId_RequesterUserId_RequestedAtUtc",
                schema: "platform",
                table: "RoleAccessRequests",
                columns: new[] { "TenantId", "RequesterUserId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleAccessRequests_TenantId_RoleId",
                schema: "platform",
                table: "RoleAccessRequests",
                columns: new[] { "TenantId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleAccessRequests_TenantId_Status_RequestedAtUtc",
                schema: "platform",
                table: "RoleAccessRequests",
                columns: new[] { "TenantId", "Status", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM [platform].[TenantRolePermissions]
                WHERE [PermissionCode] = N'workflows.manage';
                """);

            migrationBuilder.DropTable(
                name: "RoleAccessRequests",
                schema: "platform");

            migrationBuilder.DeleteData(
                schema: "platform",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "workflows.manage");
        }
    }
}
