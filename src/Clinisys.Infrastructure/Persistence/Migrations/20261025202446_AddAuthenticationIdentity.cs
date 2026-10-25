using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinisys.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticationIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "AuthRoles",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    NormalizedName = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthRoles", x => x.Id);
                    table.CheckConstraint("CK_AuthRoles_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AuthRoles_Name", "([Name] = N'Secretary' AND DATALENGTH([Name]) = 18) OR ([Name] = N'Technician' AND DATALENGTH([Name]) = 20) OR ([Name] = N'Biologist' AND DATALENGTH([Name]) = 18)");
                    table.CheckConstraint("CK_AuthRoles_NormalizedName", "[NormalizedName] = UPPER([Name]) AND DATALENGTH([NormalizedName]) = DATALENGTH([Name])");
                });

            migrationBuilder.CreateTable(
                name: "AuthUsers",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    UserName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthUsers", x => x.Id);
                    table.CheckConstraint("CK_AuthUsers_AccessFailedCount", "[AccessFailedCount] >= 0");
                    table.CheckConstraint("CK_AuthUsers_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AuthUsers_NormalizedUserName", "[NormalizedUserName] = UPPER([UserName]) AND DATALENGTH([NormalizedUserName]) = DATALENGTH([UserName])");
                    table.CheckConstraint("CK_AuthUsers_UserName", "DATALENGTH([UserName]) BETWEEN 6 AND 128 AND DATALENGTH([UserName]) = 2 * LEN([UserName]) AND LEFT([UserName],1) LIKE N'[a-zA-Z0-9]' AND [UserName] NOT LIKE N'%[^a-zA-Z0-9._-]%'");
                });

            migrationBuilder.CreateTable(
                name: "AuthRoleClaims",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthRoleClaims_AuthRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "dbo",
                        principalTable: "AuthRoles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AuthUserClaims",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthUserClaims_AuthUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "AuthUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AuthUserLogins",
                schema: "dbo",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AuthUserLogins_AuthUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "AuthUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AuthUserRoles",
                schema: "dbo",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AuthUserRoles_AuthRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "dbo",
                        principalTable: "AuthRoles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AuthUserRoles_AuthUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "AuthUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AuthUserTokens",
                schema: "dbo",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AuthUserTokens_AuthUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "AuthUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthRoleClaims_RoleId",
                schema: "dbo",
                table: "AuthRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "UX_AuthRoles_NormalizedName",
                schema: "dbo",
                table: "AuthRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthUserClaims_UserId",
                schema: "dbo",
                table: "AuthUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthUserLogins_UserId",
                schema: "dbo",
                table: "AuthUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthUserRoles_RoleId",
                schema: "dbo",
                table: "AuthUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthUsers_NormalizedEmail",
                schema: "dbo",
                table: "AuthUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UX_AuthUsers_NormalizedUserName",
                schema: "dbo",
                table: "AuthUsers",
                column: "NormalizedUserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Destructive Identity rollback is disabled. Use a reviewed forward migration.");
        }
    }
}
