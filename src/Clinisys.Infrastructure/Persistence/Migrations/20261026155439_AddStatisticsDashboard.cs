using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinisys.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStatisticsDashboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.GetStatisticsDashboard
                    @From date,
                    @To date
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF @From IS NULL OR @To IS NULL OR @From < '00010102'
                        OR @To >= '99991231' OR @From > @To OR DATEDIFF(day, @From, @To) > 365
                        THROW 51020, 'Invalid statistics period.', 1;

                    DECLARE @Start datetimeoffset(7) = TODATETIMEOFFSET(CONVERT(datetime2, @From), '+00:00');
                    DECLARE @End datetimeoffset(7) = TODATETIMEOFFSET(DATEADD(day, 1, CONVERT(datetime2, @To)), '+00:00');
                    SELECT Id, Status, CONVERT(date, SWITCHOFFSET(RegisteredAt, '+00:00')) AS RequestDay
                    INTO #Requests
                    FROM dbo.LaboratoryRequests
                    WHERE RegisteredAt >= @Start AND RegisteredAt < @End;

                    -- Same per-line rounding (half away from zero) as RequestItem.NetPrice.
                    -- Never use current catalogue prices or reapply the request discount.
                    SELECT i.LaboratoryRequestId, i.MedicalAnalysisId,
                        CONVERT(decimal(28,2), ROUND(i.UnitPriceSnapshot * (100 - i.DiscountPercentSnapshot) / 100, 2)) AS NetAmount
                    INTO #Items
                    FROM dbo.RequestItems i INNER JOIN #Requests r ON r.Id=i.LaboratoryRequestId;

                    SELECT (SELECT COUNT_BIG(*) FROM #Requests) AS RequestCount,
                        COALESCE((SELECT SUM(i.NetAmount) FROM #Items i
                            INNER JOIN #Requests r ON r.Id=i.LaboratoryRequestId WHERE r.Status IN (3,4)), 0) AS Revenue;

                    SELECT RequestDay, COUNT_BIG(*) AS RequestCount FROM #Requests
                    GROUP BY RequestDay ORDER BY RequestDay;

                    SELECT TOP (10) i.MedicalAnalysisId, a.Code, a.Name, COUNT_BIG(*) AS RequestCount
                    FROM #Items i INNER JOIN dbo.MedicalAnalyses a ON a.Id=i.MedicalAnalysisId
                    GROUP BY i.MedicalAnalysisId, a.Code, a.Name
                    ORDER BY COUNT_BIG(*) DESC, a.Code COLLATE Latin1_General_100_BIN2, i.MedicalAnalysisId;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.GetStatisticsDashboard;");
        }
    }
}
