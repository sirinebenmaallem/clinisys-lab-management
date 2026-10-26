using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinisys.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalculatedAnalysisResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCalculated",
                table: "AnalysisResults",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Calculated-result downgrade is disabled. Use a reviewed forward migration.");
        }
    }
}
