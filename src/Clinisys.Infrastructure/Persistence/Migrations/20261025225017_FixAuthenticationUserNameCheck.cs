using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinisys.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixAuthenticationUserNameCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthUsers_UserName",
                schema: "dbo",
                table: "AuthUsers");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthUsers_UserName",
                schema: "dbo",
                table: "AuthUsers",
                sql: "DATALENGTH([UserName]) BETWEEN 6 AND 128 AND DATALENGTH([UserName]) = 2 * LEN([UserName]) AND LEFT([UserName],1) LIKE N'[a-zA-Z0-9]' AND [UserName] NOT LIKE N'%[^-a-zA-Z0-9._]%'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Identity constraint downgrade is disabled. Use a reviewed forward migration.");
        }
    }
}
