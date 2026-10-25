using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Clinisys.Tests.Persistence;

public sealed class ModelConfigurationTests
{
    private static ClinisysDbContext CreateContext() => new(new DbContextOptionsBuilder<ClinisysDbContext>()
        .UseSqlServer("Server=.\\SQLEXPRESS;Database=ClinisysTestDb;Integrated Security=True;TrustServerCertificate=True").Options);

    [Fact]
    public void Model_HasTenBusinessTablesAndTechnicalCounterWithoutCascadeDelete()
    {
        using var db = CreateContext();
        Assert.Equal(18, db.Model.GetEntityTypes().Count());
        Assert.Equal(10, db.Model.GetEntityTypes().Count(e => e.ClrType.Namespace == typeof(Patient).Namespace));
        Assert.NotNull(db.Model.FindEntityType(typeof(LaboratoryRequestCounter)));
        Assert.All(db.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()),
            fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
        Assert.All(db.Model.GetEntityTypes().Where(x => typeof(Clinisys.Domain.Common.Entity).IsAssignableFrom(x.ClrType)), entity =>
        {
            var version = entity.FindProperty("Version")!;
            Assert.True(version.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
        });
        Assert.Null(db.Model.FindEntityType(typeof(LaboratoryRequestCounter))!.FindProperty("Version"));
    }

    [Fact]
    public void UniqueIndexes_ProtectCodesNumbersAndSelections()
    {
        using var db = CreateContext();
        AssertUnique<MedicalAnalysis>(db, "Code");
        AssertUnique<AnalysisPanel>(db, "Code");
        AssertUnique<Patient>(db, "NationalIdentityNumber");
        AssertUnique<LaboratoryRequest>(db, "Number");
        AssertUnique<PanelAnalysis>(db, "AnalysisPanelId", "MedicalAnalysisId");
        AssertUnique<RequestItem>(db, "LaboratoryRequestId", "MedicalAnalysisId");
        AssertUnique<RequestPanel>(db, "LaboratoryRequestId", "AnalysisPanelId");
        AssertUnique<AnalysisResult>(db, "RequestItemId");
        AssertUnique<ReferenceRange>(db, "MedicalAnalysisId", "Sex", "MinimumAgeDays", "MaximumAgeDays");
        Assert.Null(db.Model.FindEntityType(typeof(ReferenceRange))!.GetIndexes().Single(i => i.IsUnique).GetFilter());
    }

    [Fact]
    public void SqlTypes_PreserveFinancialAndClinicalPrecision()
    {
        using var db = CreateContext();
        AssertPrecision<MedicalAnalysis>(db, "Price", 18, 2);
        AssertPrecision<RequestItem>(db, "UnitPriceSnapshot", 18, 2);
        AssertPrecision<RequestItem>(db, "DiscountPercentSnapshot", 5, 2);
        AssertPrecision<LaboratoryRequest>(db, "DiscountPercent", 5, 2);
        AssertPrecision<AnalysisResult>(db, "Value", 18, 6);
        AssertPrecision<ReferenceRange>(db, "LowerBound", 18, 6);
        Assert.Equal("date", db.Model.FindEntityType(typeof(Patient))!.FindProperty("DateOfBirth")!.GetColumnType());
        Assert.Equal("datetimeoffset(7)", db.Model.FindEntityType(typeof(LaboratoryRequest))!.FindProperty("RegisteredAt")!.GetColumnType());
        Assert.Null(db.Model.FindEntityType(typeof(RequestItem))!.FindProperty("NetPrice"));
    }

    [Fact]
    public void AuditItemForeignKey_RequiresTheSameRequest()
    {
        using var db = CreateContext();
        var fk = db.Model.FindEntityType(typeof(AuditEntry))!.GetForeignKeys()
            .Single(x => x.PrincipalEntityType.ClrType == typeof(RequestItem));
        Assert.Equal(new[] { "LaboratoryRequestId", "RequestItemId" }, fk.Properties.Select(x => x.Name));
        Assert.Equal(new[] { "LaboratoryRequestId", "Id" }, fk.PrincipalKey.Properties.Select(x => x.Name));
    }

    [Fact]
    public void InitialMigration_CreatesOnlyExpectedSchemaWithoutDestructiveUpOperations()
    {
        using var db = CreateContext();
        var assembly = db.GetService<IMigrationsAssembly>();
        var entry = Assert.Single(assembly.Migrations, x => x.Key.EndsWith("_InitialCreate"));
        Assert.EndsWith("_InitialCreate", entry.Key);
        var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
        Assert.Equal(10, migration.UpOperations.OfType<CreateTableOperation>().Count());
        Assert.DoesNotContain(migration.UpOperations, op => op.IsDestructiveChange);
        Assert.DoesNotContain(migration.UpOperations, op => op is DropTableOperation or DropColumnOperation or SqlOperation);
        Assert.Throws<NotSupportedException>(() => migration.DownOperations);
        Assert.All(migration.UpOperations.OfType<CreateTableOperation>().SelectMany(t => t.ForeignKeys),
            fk => Assert.Equal(ReferentialAction.NoAction, fk.OnDelete));
        var model = db.GetService<IDesignTimeModel>().Model;
        Assert.Contains(model.FindEntityType(typeof(ReferenceRange))!.GetCheckConstraints(), c => c.Name == "CK_ReferenceRanges_Bounds");
        Assert.Contains(model.FindEntityType(typeof(AnalysisResult))!.GetCheckConstraints(), c => c.Name == "CK_AnalysisResults_ReferenceAndAbnormality");
    }

    [Fact]
    public void CounterMigration_RemainsAdditiveAndCurrentSnapshotMatchesModel()
    {
        using var db = CreateContext();
        var assembly = db.GetService<IMigrationsAssembly>();
        Assert.Equal(4, assembly.Migrations.Count);
        var entry = Assert.Single(assembly.Migrations, x => x.Key.EndsWith("_AddLaboratoryRequestCounters"));
        var migration = assembly.CreateMigration(entry.Value, db.Database.ProviderName!);
        var table = Assert.IsType<CreateTableOperation>(Assert.Single(migration.UpOperations));
        Assert.Equal("LaboratoryRequestCounters", table.Name);
        Assert.Equal(new[] { "Year", "LastValue" }, table.Columns.Select(x => x.Name));
        Assert.Equal(new[] { "Year" }, table.PrimaryKey!.Columns);
        Assert.Equal(2, table.CheckConstraints.Count);
        Assert.Throws<NotSupportedException>(() => migration.DownOperations);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static void AssertUnique<T>(ClinisysDbContext db, params string[] properties) =>
        Assert.Contains(db.Model.FindEntityType(typeof(T))!.GetIndexes(),
            i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(properties));

    private static void AssertPrecision<T>(ClinisysDbContext db, string name, int precision, int scale)
    {
        var property = db.Model.FindEntityType(typeof(T))!.FindProperty(name)!;
        Assert.Equal(precision, property.GetPrecision());
        Assert.Equal(scale, property.GetScale());
    }
}
