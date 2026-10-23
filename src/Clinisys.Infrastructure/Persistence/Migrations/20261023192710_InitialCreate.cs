using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clinisys.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalysisPanels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisPanels", x => x.Id);
                    table.CheckConstraint("CK_AnalysisPanels_Code", "LEN(LTRIM(RTRIM([Code]))) > 0");
                    table.CheckConstraint("CK_AnalysisPanels_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AnalysisPanels_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                });

            migrationBuilder.CreateTable(
                name: "MedicalAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalAnalyses", x => x.Id);
                    table.CheckConstraint("CK_MedicalAnalyses_Code", "LEN(LTRIM(RTRIM([Code]))) > 0");
                    table.CheckConstraint("CK_MedicalAnalyses_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_MedicalAnalyses_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_MedicalAnalyses_Price", "[Price] >= 0");
                    table.CheckConstraint("CK_MedicalAnalyses_Unit", "LEN(LTRIM(RTRIM([Unit]))) > 0");
                });

            migrationBuilder.CreateTable(
                name: "Patients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    Sex = table.Column<int>(type: "int", nullable: false),
                    NationalIdentityNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patients", x => x.Id);
                    table.CheckConstraint("CK_Patients_DateOfBirth", "[DateOfBirth] > '0001-01-01' AND [DateOfBirth] <= CONVERT(date, SYSUTCDATETIME())");
                    table.CheckConstraint("CK_Patients_FirstName", "LEN(LTRIM(RTRIM([FirstName]))) > 0");
                    table.CheckConstraint("CK_Patients_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Patients_Identity", "[NationalIdentityNumber] IS NOT NULL OR [PhoneNumber] IS NOT NULL");
                    table.CheckConstraint("CK_Patients_LastName", "LEN(LTRIM(RTRIM([LastName]))) > 0");
                    table.CheckConstraint("CK_Patients_NationalIdentityNumber", "[NationalIdentityNumber] IS NULL OR LEN(LTRIM(RTRIM([NationalIdentityNumber]))) > 0");
                    table.CheckConstraint("CK_Patients_PhoneNumber", "[PhoneNumber] IS NULL OR LEN(LTRIM(RTRIM([PhoneNumber]))) > 0");
                    table.CheckConstraint("CK_Patients_Sex", "[Sex] IN (0,1,2)");
                });

            migrationBuilder.CreateTable(
                name: "PanelAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnalysisPanelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MedicalAnalysisId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PanelAnalyses", x => x.Id);
                    table.CheckConstraint("CK_PanelAnalyses_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_PanelAnalyses_AnalysisPanels_AnalysisPanelId",
                        column: x => x.AnalysisPanelId,
                        principalTable: "AnalysisPanels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PanelAnalyses_MedicalAnalyses_MedicalAnalysisId",
                        column: x => x.MedicalAnalysisId,
                        principalTable: "MedicalAnalyses",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReferenceRanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MedicalAnalysisId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sex = table.Column<int>(type: "int", nullable: false),
                    MinimumAgeDays = table.Column<int>(type: "int", nullable: false),
                    MaximumAgeDays = table.Column<int>(type: "int", nullable: true),
                    LowerBound = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UpperBound = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferenceRanges", x => x.Id);
                    table.CheckConstraint("CK_ReferenceRanges_Age", "[MinimumAgeDays] >= 0 AND ([MaximumAgeDays] IS NULL OR [MaximumAgeDays] > [MinimumAgeDays])");
                    table.CheckConstraint("CK_ReferenceRanges_Bounds", "[LowerBound] <= [UpperBound]");
                    table.CheckConstraint("CK_ReferenceRanges_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ReferenceRanges_Sex", "[Sex] IN (0,1,2)");
                    table.ForeignKey(
                        name: "FK_ReferenceRanges_MedicalAnalyses_MedicalAnalysisId",
                        column: x => x.MedicalAnalysisId,
                        principalTable: "MedicalAnalyses",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LaboratoryRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientDateOfBirthSnapshot = table.Column<DateOnly>(type: "date", nullable: false),
                    PatientSexSnapshot = table.Column<int>(type: "int", nullable: false),
                    PrescribingPhysician = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ValidatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LaboratoryRequests", x => x.Id);
                    table.CheckConstraint("CK_LaboratoryRequests_Discount", "[DiscountPercent] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_LaboratoryRequests_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_LaboratoryRequests_Number", "LEN(LTRIM(RTRIM([Number]))) > 0");
                    table.CheckConstraint("CK_LaboratoryRequests_PatientSnapshot", "[PatientSexSnapshot] IN (0,1,2) AND [PatientDateOfBirthSnapshot] > '0001-01-01' AND [PatientDateOfBirthSnapshot] <= CONVERT(date,[RegisteredAt])");
                    table.CheckConstraint("CK_LaboratoryRequests_PrescribingPhysician", "LEN(LTRIM(RTRIM([PrescribingPhysician]))) > 0");
                    table.CheckConstraint("CK_LaboratoryRequests_Status", "[Status] IN (0,1,2,3,4)");
                    table.CheckConstraint("CK_LaboratoryRequests_ValidatedBy", "[ValidatedBy] IS NULL OR LEN(LTRIM(RTRIM([ValidatedBy]))) > 0");
                    table.CheckConstraint("CK_LaboratoryRequests_Validation", "([Status] IN (0,1,2) AND [ValidatedAt] IS NULL AND [ValidatedBy] IS NULL AND [DeliveredAt] IS NULL) OR ([Status] IN (3,4) AND [ValidatedAt] IS NOT NULL AND [ValidatedBy] IS NOT NULL AND [ValidatedAt] >= [RegisteredAt] AND (([Status] = 3 AND [DeliveredAt] IS NULL) OR ([Status] = 4 AND [DeliveredAt] IS NOT NULL AND [DeliveredAt] >= [ValidatedAt])))");
                    table.ForeignKey(
                        name: "FK_LaboratoryRequests_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RequestItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LaboratoryRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MedicalAnalysisId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnalysisCodeSnapshot = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AnalysisNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UnitSnapshot = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UnitPriceSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountPercentSnapshot = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestItems", x => x.Id);
                    table.UniqueConstraint("AK_RequestItems_LaboratoryRequestId_Id", x => new { x.LaboratoryRequestId, x.Id });
                    table.CheckConstraint("CK_RequestItems_AnalysisCodeSnapshot", "LEN(LTRIM(RTRIM([AnalysisCodeSnapshot]))) > 0");
                    table.CheckConstraint("CK_RequestItems_AnalysisNameSnapshot", "LEN(LTRIM(RTRIM([AnalysisNameSnapshot]))) > 0");
                    table.CheckConstraint("CK_RequestItems_Discount", "[DiscountPercentSnapshot] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_RequestItems_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_RequestItems_Price", "[UnitPriceSnapshot] >= 0");
                    table.CheckConstraint("CK_RequestItems_UnitSnapshot", "LEN(LTRIM(RTRIM([UnitSnapshot]))) > 0");
                    table.ForeignKey(
                        name: "FK_RequestItems_LaboratoryRequests_LaboratoryRequestId",
                        column: x => x.LaboratoryRequestId,
                        principalTable: "LaboratoryRequests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RequestItems_MedicalAnalyses_MedicalAnalysisId",
                        column: x => x.MedicalAnalysisId,
                        principalTable: "MedicalAnalyses",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RequestPanels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LaboratoryRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnalysisPanelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PanelCodeSnapshot = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PanelNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestPanels", x => x.Id);
                    table.CheckConstraint("CK_RequestPanels_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_RequestPanels_PanelCodeSnapshot", "LEN(LTRIM(RTRIM([PanelCodeSnapshot]))) > 0");
                    table.CheckConstraint("CK_RequestPanels_PanelNameSnapshot", "LEN(LTRIM(RTRIM([PanelNameSnapshot]))) > 0");
                    table.ForeignKey(
                        name: "FK_RequestPanels_AnalysisPanels_AnalysisPanelId",
                        column: x => x.AnalysisPanelId,
                        principalTable: "AnalysisPanels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RequestPanels_LaboratoryRequests_LaboratoryRequestId",
                        column: x => x.LaboratoryRequestId,
                        principalTable: "LaboratoryRequests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AnalysisResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ReferenceLowerBoundSnapshot = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    ReferenceUpperBoundSnapshot = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Abnormality = table.Column<int>(type: "int", nullable: false),
                    EnteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    EnteredBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ValidatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisResults", x => x.Id);
                    table.CheckConstraint("CK_AnalysisResults_EnteredBy", "LEN(LTRIM(RTRIM([EnteredBy]))) > 0");
                    table.CheckConstraint("CK_AnalysisResults_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AnalysisResults_ReferenceAndAbnormality", "([ReferenceLowerBoundSnapshot] IS NULL AND [ReferenceUpperBoundSnapshot] IS NULL AND [Abnormality] = 0) OR ([ReferenceLowerBoundSnapshot] IS NOT NULL AND [ReferenceUpperBoundSnapshot] IS NOT NULL AND [ReferenceLowerBoundSnapshot] <= [ReferenceUpperBoundSnapshot] AND (([Abnormality] = 1 AND [Value] BETWEEN [ReferenceLowerBoundSnapshot] AND [ReferenceUpperBoundSnapshot]) OR ([Abnormality] = 2 AND [Value] < [ReferenceLowerBoundSnapshot]) OR ([Abnormality] = 3 AND [Value] > [ReferenceUpperBoundSnapshot])))");
                    table.CheckConstraint("CK_AnalysisResults_ValidatedBy", "[ValidatedBy] IS NULL OR LEN(LTRIM(RTRIM([ValidatedBy]))) > 0");
                    table.CheckConstraint("CK_AnalysisResults_Validation", "([ValidatedAt] IS NULL AND [ValidatedBy] IS NULL) OR ([ValidatedAt] IS NOT NULL AND [ValidatedBy] IS NOT NULL AND [ValidatedAt] >= [EnteredAt])");
                    table.ForeignKey(
                        name: "FK_AnalysisResults_RequestItems_RequestItemId",
                        column: x => x.RequestItemId,
                        principalTable: "RequestItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LaboratoryRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<int>(type: "int", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                    table.CheckConstraint("CK_AuditEntries_Action", "[Action] IN (0,1,2,3,4)");
                    table.CheckConstraint("CK_AuditEntries_Actor", "LEN(LTRIM(RTRIM([Actor]))) > 0");
                    table.CheckConstraint("CK_AuditEntries_FieldName", "LEN(LTRIM(RTRIM([FieldName]))) > 0");
                    table.CheckConstraint("CK_AuditEntries_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AuditEntries_NewValue", "[NewValue] IS NULL OR LEN(LTRIM(RTRIM([NewValue]))) > 0");
                    table.CheckConstraint("CK_AuditEntries_OldValue", "[OldValue] IS NULL OR LEN(LTRIM(RTRIM([OldValue]))) > 0");
                    table.CheckConstraint("CK_AuditEntries_Values", "[OldValue] IS NOT NULL OR [NewValue] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_AuditEntries_LaboratoryRequests_LaboratoryRequestId",
                        column: x => x.LaboratoryRequestId,
                        principalTable: "LaboratoryRequests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AuditEntries_RequestItems_LaboratoryRequestId_RequestItemId",
                        columns: x => new { x.LaboratoryRequestId, x.RequestItemId },
                        principalTable: "RequestItems",
                        principalColumns: new[] { "LaboratoryRequestId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisPanels_Code",
                table: "AnalysisPanels",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisResults_RequestItemId",
                table: "AnalysisResults",
                column: "RequestItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_LaboratoryRequestId_OccurredAt",
                table: "AuditEntries",
                columns: new[] { "LaboratoryRequestId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_LaboratoryRequestId_RequestItemId",
                table: "AuditEntries",
                columns: new[] { "LaboratoryRequestId", "RequestItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_LaboratoryRequests_Number",
                table: "LaboratoryRequests",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LaboratoryRequests_PatientId",
                table: "LaboratoryRequests",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_LaboratoryRequests_RegisteredAt",
                table: "LaboratoryRequests",
                column: "RegisteredAt");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalAnalyses_Code",
                table: "MedicalAnalyses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PanelAnalyses_AnalysisPanelId_MedicalAnalysisId",
                table: "PanelAnalyses",
                columns: new[] { "AnalysisPanelId", "MedicalAnalysisId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PanelAnalyses_MedicalAnalysisId",
                table: "PanelAnalyses",
                column: "MedicalAnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_NationalIdentityNumber",
                table: "Patients",
                column: "NationalIdentityNumber",
                unique: true,
                filter: "[NationalIdentityNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_PhoneNumber",
                table: "Patients",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceRanges_MedicalAnalysisId_Sex_MinimumAgeDays_MaximumAgeDays",
                table: "ReferenceRanges",
                columns: new[] { "MedicalAnalysisId", "Sex", "MinimumAgeDays", "MaximumAgeDays" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_LaboratoryRequestId_MedicalAnalysisId",
                table: "RequestItems",
                columns: new[] { "LaboratoryRequestId", "MedicalAnalysisId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_MedicalAnalysisId",
                table: "RequestItems",
                column: "MedicalAnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPanels_AnalysisPanelId",
                table: "RequestPanels",
                column: "AnalysisPanelId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPanels_LaboratoryRequestId_AnalysisPanelId",
                table: "RequestPanels",
                columns: new[] { "LaboratoryRequestId", "AnalysisPanelId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Destructive rollback is disabled to protect medical history. Use a reviewed forward migration.");
        }
    }
}
