using System.Data.Common;
using System.Reflection;
using Clinisys.Infrastructure;
using Clinisys.Infrastructure.Authentication;
using Clinisys.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clinisys.Tests.Authentication;

// Metadata only. The interceptor throws BEFORE any attempt to open a connection.
public sealed class IdentityPersistenceTests
{
    private const string Connection = "Server=.\\SQLEXPRESS;Database=ClinisysTestDb;Integrated Security=True;TrustServerCertificate=True";
    private static readonly string[] Tables = ["AuthRoleClaims", "AuthRoles", "AuthUserClaims", "AuthUserLogins", "AuthUserRoles", "AuthUserTokens", "AuthUsers"];

    private static ClinisysDbContext Context(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<ClinisysDbContext>().UseSqlServer(Connection)
            .AddInterceptors(interceptor ?? new BlockConnection());
        return new(options.Options);
    }

    [Fact]
    public void Identity_HasExactlySevenTablesGuidKeysAndNoMedicalRelationsOrCascade()
    {
        using var db = Context();
        var identity = db.Model.GetEntityTypes().Where(e => e.GetTableName()!.StartsWith("Auth", StringComparison.Ordinal)).ToArray();
        Assert.Equal(Tables, identity.Select(e => e.GetTableName()).Order(StringComparer.Ordinal));
        Assert.All(identity, e => Assert.Equal("dbo", e.GetSchema()));
        Assert.Equal(typeof(Guid), db.Model.FindEntityType(typeof(ApplicationUser))!.FindProperty("Id")!.ClrType);
        Assert.Equal(typeof(Guid), db.Model.FindEntityType(typeof(ApplicationRole))!.FindProperty("Id")!.ClrType);
        Assert.All(identity.SelectMany(e => e.GetForeignKeys()), fk =>
        {
            Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior);
            Assert.Contains(fk.PrincipalEntityType, identity);
        });
        Assert.DoesNotContain(db.Model.GetEntityTypes(), e => e.ClrType.Name.Contains("Passkey", StringComparison.OrdinalIgnoreCase));
        Assert.All(db.GetService<IDesignTimeModel>().Model.GetEntityTypes(), e => Assert.Empty(e.GetSeedData()));
    }

    [Fact]
    public void RequiredNamesAndUniqueUnfilteredIndexes_AreMappedWithoutReplacingStampsByRowversion()
    {
        using var db = Context();
        var user = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ApplicationUser))!;
        foreach (var name in new[] { "UserName", "NormalizedUserName" })
        {
            Assert.False(user.FindProperty(name)!.IsNullable);
            Assert.Equal(64, user.FindProperty(name)!.GetMaxLength());
            Assert.Equal("Latin1_General_100_BIN2", user.FindProperty(name)!.GetCollation());
        }
        var index = Assert.Single(user.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(["NormalizedUserName"]));
        Assert.True(index.IsUnique);
        Assert.Null(index.GetFilter());
        Assert.Equal("UX_AuthUsers_NormalizedUserName", index.GetDatabaseName());
        Assert.Equal(false, user.FindProperty("IsActive")!.GetDefaultValue());
        Assert.False(user.FindProperty("IsActive")!.IsNullable);
        Assert.True(user.FindProperty("ConcurrencyStamp")!.IsConcurrencyToken);
        Assert.False(user.FindProperty("SecurityStamp")!.IsNullable);
        Assert.Null(user.FindProperty("Version"));
        Assert.Contains(user.GetCheckConstraints(), c => c.Name == "CK_AuthUsers_NormalizedUserName");
        var role = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ApplicationRole))!;
        Assert.Contains(role.GetCheckConstraints(), c => c.Name == "CK_AuthRoles_Name");
        Assert.True(Assert.Single(role.GetIndexes()).IsUnique);
        Assert.Null(Assert.Single(role.GetIndexes()).GetFilter());
        var membership = db.Model.FindEntityType(typeof(IdentityUserRole<Guid>))!;
        Assert.Equal(new[] { "UserId", "RoleId" }, membership.FindPrimaryKey()!.Properties.Select(p => p.Name));
    }

    [Fact]
    public void ThirdMigration_IsExclusivelyAdditiveIdentitySchemaAndMatchesSnapshot()
    {
        using var db = Context();
        var assembly = db.GetService<IMigrationsAssembly>();
        Assert.Equal(new[] { "20261023192710_InitialCreate", "20261024175903_AddLaboratoryRequestCounters",
            "20261025202446_AddAuthenticationIdentity", "20261025225017_FixAuthenticationUserNameCheck", "20261025235303_AddCalculatedAnalysisResult" }, assembly.Migrations.Keys.Order(StringComparer.Ordinal));
        var previous = db.GetService<IModelRuntimeInitializer>().Initialize(assembly.ModelSnapshot!.Model, designTime: true);
        var current = db.GetService<IDesignTimeModel>().Model;
        var diff = db.GetService<IMigrationsModelDiffer>().GetDifferences(previous.GetRelationalModel(), current.GetRelationalModel());
        Assert.Empty(diff);
        Assert.False(db.Database.HasPendingModelChanges());
        var migration = assembly.CreateMigration(assembly.Migrations["20261025202446_AddAuthenticationIdentity"], db.Database.ProviderName!);
        var operations = migration.UpOperations;
        Assert.Equal(15, operations.Count);
        var tables = operations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(Tables, tables.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.Equal(7, operations.OfType<CreateIndexOperation>().Count());
        Assert.Equal(6, tables.Sum(t => t.ForeignKeys.Count));
        Assert.Equal(7, tables.Sum(t => t.CheckConstraints.Count));
        Assert.All(tables, table =>
        {
            Assert.Equal("dbo", table.Schema);
            Assert.NotNull(table.PrimaryKey);
            Assert.All(table.ForeignKeys, fk =>
            {
                Assert.Equal("dbo", fk.PrincipalSchema);
                Assert.Contains(fk.PrincipalTable, Tables);
                Assert.Equal(ReferentialAction.NoAction, fk.OnDelete);
            });
        });
        Assert.Throws<NotSupportedException>(() => migration.DownOperations);
        var userTable = Assert.Single(tables, t => t.Name == "AuthUsers");
        Assert.Equal(false, Assert.Single(userTable.Columns, c => c.Name == "IsActive").DefaultValue);
        var userNameIndex = Assert.Single(operations.OfType<CreateIndexOperation>(), i => i.Name == "UX_AuthUsers_NormalizedUserName");
        Assert.True(userNameIndex.IsUnique);
        Assert.Null(userNameIndex.Filter);
        Assert.All(operations, op =>
        {
            Assert.False(op.IsDestructiveChange);
            Assert.True(op is CreateTableOperation or CreateIndexOperation or EnsureSchemaOperation, $"Unexpected change: {op.GetType().Name}");
            if (op is EnsureSchemaOperation schema) Assert.Equal("dbo", schema.Name);
            if (op is CreateIndexOperation index) Assert.Contains(index.Table, Tables);
        });
    }

    [Fact]
    public void FourthMigration_ReplacesOnlyUserNameCheckAndPreservesAllOtherMappings()
    {
        using var db = Context();
        var assembly = db.GetService<IMigrationsAssembly>();
        var correction = assembly.CreateMigration(assembly.Migrations["20261025225017_FixAuthenticationUserNameCheck"], db.Database.ProviderName!);
        Assert.Equal(2, correction.UpOperations.Count);
        var drop = Assert.IsType<DropCheckConstraintOperation>(correction.UpOperations[0]);
        var add = Assert.IsType<AddCheckConstraintOperation>(correction.UpOperations[1]);
        Assert.Equal("CK_AuthUsers_UserName", drop.Name);
        Assert.Equal("CK_AuthUsers_UserName", add.Name);
        Assert.Equal("dbo", drop.Schema); Assert.Equal("dbo", add.Schema);
        Assert.Equal("AuthUsers", drop.Table); Assert.Equal("AuthUsers", add.Table);

        var identity = assembly.CreateMigration(assembly.Migrations["20261025202446_AddAuthenticationIdentity"], db.Database.ProviderName!);
        var original = Assert.Single(identity.UpOperations.OfType<CreateTableOperation>(), t => t.Name == "AuthUsers")
            .CheckConstraints.Single(c => c.Name == "CK_AuthUsers_UserName").Sql;
        Assert.Equal(original.Replace("%[^a-zA-Z0-9._-]%", "%[^-a-zA-Z0-9._]%", StringComparison.Ordinal), add.Sql);
        Assert.Throws<NotSupportedException>(() => correction.DownOperations);

        var initializer = db.GetService<IModelRuntimeInitializer>();
        var previous = initializer.Initialize(identity.TargetModel, designTime: true);
        var current = initializer.Initialize(correction.TargetModel, designTime: true);
        var difference = db.GetService<IMigrationsModelDiffer>().GetDifferences(previous.GetRelationalModel(), current.GetRelationalModel());
        Assert.Equal(2, difference.Count);
        Assert.IsType<DropCheckConstraintOperation>(difference[0]);
        var modelChange = Assert.IsType<AddCheckConstraintOperation>(difference[1]);
        Assert.Equal(add.Sql, modelChange.Sql);
        Assert.Equal(add.Table, modelChange.Table);
        Assert.Equal(add.Schema, modelChange.Schema);
        Assert.Equal(add.Name, modelChange.Name);
    }

    [Fact]
    public void DirectDiFactoryAndIndependentContexts_HaveIdenticalRelationalModelsAndPreserveOptions()
    {
        var guard = new BlockConnection();
        using var direct = Context(guard);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:ClinisysDb"] = Connection }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(config);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var di = scope.ServiceProvider.GetRequiredService<ClinisysDbContext>();
        // Model access and the design-time factory do not open SQL connections.
        var key = "ConnectionStrings__ClinisysDb";
        var previous = Environment.GetEnvironmentVariable(key);
        ClinisysDbContext factory;
        try
        {
            Environment.SetEnvironmentVariable(key, Connection);
            factory = new ClinisysDbContextFactory().CreateDbContext([]);
        }
        finally { Environment.SetEnvironmentVariable(key, previous); }
        using (factory)
        using (var independent = (ClinisysDbContext)typeof(ClinisysDbContext)
            .GetMethod("CreateIndependentContext", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(direct, null)!)
        {
            var differ = direct.GetService<IMigrationsModelDiffer>();
            foreach (var other in new[] { di, factory, independent })
                Assert.Empty(differ.GetDifferences(direct.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
                    other.GetService<IDesignTimeModel>().Model.GetRelationalModel()));
            Assert.Same(direct.GetService<IDbContextOptions>(), independent.GetService<IDbContextOptions>());
            Assert.False(independent.Database.CreateExecutionStrategy().RetriesOnFailure);
            Assert.Equal(direct.Database.GetConnectionString(), independent.Database.GetConnectionString());
            Assert.Throws<ConnectionBlocked>(() => independent.Database.OpenConnection());
            Assert.Equal(1, guard.Attempts);
        }
    }

    private sealed class ConnectionBlocked : Exception { }
    private sealed class BlockConnection : DbConnectionInterceptor
    {
        public int Attempts { get; private set; }
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        { Attempts++; throw new ConnectionBlocked(); }
    }
}
