using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Platform.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantRolesAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Memberships_TenantId_Id",
                schema: "platform",
                table: "Memberships",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "Permissions",
                schema: "platform",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "TenantRoles",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantRoles", x => x.Id);
                    table.UniqueConstraint("AK_TenantRoles_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_TenantRoles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "platform",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MembershipRoles",
                schema: "platform",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipRoles", x => new { x.TenantId, x.MembershipId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_MembershipRoles_Memberships_TenantId_MembershipId",
                        columns: x => new { x.TenantId, x.MembershipId },
                        principalSchema: "platform",
                        principalTable: "Memberships",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MembershipRoles_TenantRoles_TenantId_RoleId",
                        columns: x => new { x.TenantId, x.RoleId },
                        principalSchema: "platform",
                        principalTable: "TenantRoles",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantRolePermissions",
                schema: "platform",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantRolePermissions", x => new { x.TenantId, x.RoleId, x.PermissionCode });
                    table.ForeignKey(
                        name: "FK_TenantRolePermissions_Permissions_PermissionCode",
                        column: x => x.PermissionCode,
                        principalSchema: "platform",
                        principalTable: "Permissions",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantRolePermissions_TenantRoles_TenantId_RoleId",
                        columns: x => new { x.TenantId, x.RoleId },
                        principalSchema: "platform",
                        principalTable: "TenantRoles",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "platform",
                table: "Permissions",
                columns: new[] { "Code", "Description" },
                values: new object[,]
                {
                    { "members.manage", "Manage tenant members." },
                    { "members.read", "Read tenant members." },
                    { "roles.manage", "Manage tenant roles and assignments." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipRoles_TenantId_RoleId",
                schema: "platform",
                table: "MembershipRoles",
                columns: new[] { "TenantId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantRolePermissions_PermissionCode",
                schema: "platform",
                table: "TenantRolePermissions",
                column: "PermissionCode");

            migrationBuilder.CreateIndex(
                name: "IX_TenantRoles_TenantId_Name",
                schema: "platform",
                table: "TenantRoles",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MembershipRoles",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "TenantRolePermissions",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "Permissions",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "TenantRoles",
                schema: "platform");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Memberships_TenantId_Id",
                schema: "platform",
                table: "Memberships");
        }
    }
}
