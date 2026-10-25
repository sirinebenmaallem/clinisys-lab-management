using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinisys.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLaboratoryRequestCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LaboratoryRequestCounters",
                columns: table => new
                {
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastValue = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LaboratoryRequestCounters", x => x.Year);
                    table.CheckConstraint("CK_LaboratoryRequestCounters_LastValue", "[LastValue] BETWEEN 1 AND 999999");
                    table.CheckConstraint("CK_LaboratoryRequestCounters_Year", "[Year] BETWEEN 1 AND 9999");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Destructive counter rollback is disabled. Use a reviewed forward migration.");
        }
    }
}
