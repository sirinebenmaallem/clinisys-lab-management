using System.Data.Common;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Transactions;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure;
using Clinisys.Infrastructure.AnalysisResults;
using Clinisys.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clinisys.Tests.AnalysisResults;

// All connection opens are intercepted BEFORE the physical SqlConnection.Open.
// Metadata/tracking tests below never save changes or execute database commands.
public sealed class AnalysisResultStoreUnitTests
{
    private static readonly byte[] Version = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();
    private static readonly DateTimeOffset Instant = DateTimeOffset.UnixEpoch.AddMinutes(5);

    [Fact]
    public void MissingActorProvider_IsRejectedAndDependencyInjectionHasNoDefaultActor()
    {
        using var db = Context(new OpeningFailure(new ConnectionBlocked()));
        Assert.Throws<ArgumentNullException>(() => new AnalysisResultStore(db, TimeProvider.System, null!));
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:ClinisysDb"] = Connection }).Build());
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IAnalysisResultActorProvider));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IAnalysisResultStore>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("too-long")]
    public async Task InvalidActor_RejectsBothWritesBeforeOpeningConnection(string? actor)
    {
        var guard = new OpeningFailure(new ConnectionBlocked());
        await using var db = Context(guard);
        var source = new Actor(actor == "too-long" ? new string('x', 101) : actor!);
        var store = new AnalysisResultStore(db, TimeProvider.System, source);
        foreach (var update in new[] { false, true })
        {
            var error = await Assert.ThrowsAsync<AnalysisResultException>(() => update
                ? store.UpdateAsync(RequestId, ItemId, 1, Version, default)
                : store.CreateAsync(RequestId, ItemId, 1, default));
            Assert.Equal(AnalysisResultError.Internal, error.Error);
        }
        Assert.Equal(2, source.Calls);
        Assert.Equal(0, guard.Attempts);
    }

    [Fact]
    public async Task ProviderFailure_IsNotReplacedWithAnAnonymousActor()
    {
        var guard = new OpeningFailure(new ConnectionBlocked());
        await using var db = Context(guard);
        var failure = new InvalidOperationException("Synthetic actor unavailable");
        var store = new AnalysisResultStore(db, TimeProvider.System, new FailingActor(failure));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(RequestId, ItemId, 1, default)));
        Assert.Equal(0, guard.Attempts);
    }

    [Fact]
    public async Task Writes_RejectAmbientTransactionsAndConfiguredRetriesBeforeConnection()
    {
        var guard = new OpeningFailure(new ConnectionBlocked());
        await using var db = Context(guard);
        var store = new AnalysisResultStore(db, TimeProvider.System, new Actor("synthetic"));
        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(RequestId, ItemId, 1, default));
        await using var retryDb = Context(guard, retry: true);
        var retryStore = new AnalysisResultStore(retryDb, TimeProvider.System, new Actor("synthetic"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => retryStore.CreateAsync(RequestId, ItemId, 1, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => retryStore.GetAsync(RequestId, ItemId, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => retryStore.ListForRequestAsync(RequestId, default));
        Assert.Equal(0, guard.Attempts);
    }

    [Fact]
    public async Task InvalidVersion_IsRejectedBeforeActorOrConnection()
    {
        var guard = new OpeningFailure(new ConnectionBlocked());
        await using var db = Context(guard);
        var actor = new Actor("synthetic");
        var store = new AnalysisResultStore(db, TimeProvider.System, actor);
        foreach (var version in new byte[]?[] { null, [], new byte[7], new byte[9] })
            Assert.Equal(AnalysisResultError.Validation, (await Assert.ThrowsAsync<AnalysisResultException>(() =>
                store.UpdateAsync(RequestId, ItemId, 1, version!, default))).Error);
        Assert.Equal(0, guard.Attempts);
        Assert.Equal(0, actor.Calls);
    }

    [Theory]
    [InlineData(1205, false)]
    [InlineData(1205, true)]
    [InlineData(547, true)]
    [InlineData(-2, false)]
    [InlineData(2627, true)]
    public async Task AllOperations_ClassifyDeadlocksOnly_WithoutRetryOrPhysicalConnection(int number, bool wrapped)
    {
        Exception failure = SqlFailure(number);
        if (wrapped) failure = new InvalidOperationException("Synthetic EF", new DbUpdateException("Synthetic", failure));
        foreach (var operation in new[] { "create", "update", "get", "list" })
        {
            var guard = new OpeningFailure(failure);
            await using var db = Context(guard);
            var actor = new Actor("synthetic");
            var store = new AnalysisResultStore(db, TimeProvider.System, actor);
            var error = await Record.ExceptionAsync(async () =>
            {
                switch (operation)
                {
                    case "create": await store.CreateAsync(RequestId, ItemId, 1, default); break;
                    case "update": await store.UpdateAsync(RequestId, ItemId, 1, Version, default); break;
                    case "get": await store.GetAsync(RequestId, ItemId, default); break;
                    default: await store.ListForRequestAsync(RequestId, default); break;
                }
            });
            Assert.Equal(1, guard.Attempts);
            if (number == 1205) Assert.Equal(AnalysisResultError.Conflict, Assert.IsType<AnalysisResultException>(error).Error);
            else Assert.Same(failure, error);
            Assert.Equal(operation is "create" or "update" ? 1 : 0, actor.Calls);
        }
    }

    [Fact]
    public async Task OwnedContext_PreservesConnectionGuardAndNeverFallsBackToConfiguration()
    {
        var guard = new OpeningFailure(new ConnectionBlocked());
        await using var db = Context(guard);
        await Assert.ThrowsAsync<ConnectionBlocked>(() => new AnalysisResultStore(db, TimeProvider.System, new Actor("synthetic"))
            .CreateAsync(RequestId, ItemId, 0, default));
        Assert.Equal(1, guard.Attempts);
    }

    [Theory]
    [InlineData(1)] // Same numeric value still requires a versioned UPDATE.
    [InlineData(3)]
    public void PrepareCorrection_UsesClientOriginalVersionAndOnlyMarksCorrectedFields(decimal value)
    {
        using var db = Context(new OpeningFailure(new ConnectionBlocked()));
        var result = Result();
        var unrelated = new Patient("Synthetic", "Unrelated", new(2000, 1, 1), Sex.Unspecified, phoneNumber: "TEST-ONLY");
        db.Attach(unrelated);
        Invoke("PrepareCorrection", db, result, value, Version.ToArray(), "new-synthetic-actor", Instant);
        var entry = db.Entry(result);
        Assert.Equal(Version, entry.Property(x => x.Version).OriginalValue);
        Assert.Equal(new[] { "Abnormality", "EnteredAt", "EnteredBy", "Value" },
            entry.Properties.Where(x => x.IsModified).Select(x => x.Metadata.Name).Order().ToArray());
        Assert.Equal(1, result.ReferenceLowerBoundSnapshot);
        Assert.Equal(2, result.ReferenceUpperBoundSnapshot);
        Assert.Equal(ItemId, result.RequestItemId);
        Assert.Equal("new-synthetic-actor", result.EnteredBy);
        Assert.Equal(Instant, result.EnteredAt);
        Assert.Equal(value == 3 ? Abnormality.High : Abnormality.Normal, result.Abnormality);
        Assert.Equal(EntityState.Unchanged, db.Entry(unrelated).State);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("validated-date")]
    [InlineData("validated-actor")]
    [InlineData("invalid-value")]
    public void RejectedCorrection_DoesNotMutateOrAttachResult(string reason)
    {
        using var db = Context(new OpeningFailure(new ConnectionBlocked()));
        var result = Result();
        if (reason == "validated-date") Set(result, nameof(result.ValidatedAt), Instant);
        if (reason == "validated-actor") Set(result, nameof(result.ValidatedBy), "synthetic-validator");
        var before = (result.Value, result.EnteredBy, result.EnteredAt, result.Abnormality);
        var version = reason == "stale" ? new byte[8] : Version;
        var error = Assert.Throws<AnalysisResultException>(() => Invoke("PrepareCorrection", db, result,
            reason == "invalid-value" ? 0.0000001m : 3m, version, "corrector", Instant));
        Assert.Equal(reason == "invalid-value" ? AnalysisResultError.Validation : AnalysisResultError.Conflict, error.Error);
        Assert.Equal(before, (result.Value, result.EnteredBy, result.EnteredAt, result.Abnormality));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(1205)]
    [InlineData(2601)]
    [InlineData(2627)]
    public void WriteFailure_TranslatesConfirmedDeadlockAndUniqueness(int number)
    {
        var error = Invoke("TranslateWriteFailure", new InvalidOperationException("EF", new DbUpdateException("EF", SqlFailure(number))));
        Assert.Equal(AnalysisResultError.Conflict, Assert.IsType<AnalysisResultException>(error).Error);
    }

    [Fact]
    public void OtherFailures_AreNotDisguisedAsConflicts()
    {
        foreach (var failure in new Exception[] { new InvalidOperationException("Synthetic"),
            new DbUpdateException("Synthetic", SqlFailure(547)), AnalysisResultException.Unknown() })
            Assert.Same(failure, Invoke("TranslateWriteFailure", failure));
        Assert.Equal(AnalysisResultError.Conflict,
            Assert.IsType<AnalysisResultException>(Invoke("TranslateWriteFailure", new DbUpdateConcurrencyException())).Error);
    }

    [Fact]
    public void Detail_UsesHistoricalSnapshotsAndStoredResultWithoutPatientOrCatalogueRead()
    {
        var patient = new Patient("Synthetic", "Test", new(2000, 1, 1), Sex.Female, phoneNumber: "TEST-ONLY");
        var request = new LaboratoryRequest("SYN", patient, "Synthetic");
        var analysis = new MedicalAnalysis("OLD", "Historical", "old-unit", 10);
        var item = new RequestItem(request, analysis);
        var result = new AnalysisResult(item.Id, 1, "synthetic", new(0, 2), DateTimeOffset.UnixEpoch);
        Set(result, nameof(result.Version), Version.ToArray());
        analysis.Update("NEW", "Current", "new-unit", 20);
        var dto = Assert.IsType<AnalysisResultDetailDto>(Invoke("Detail", item, result));
        Assert.Equal("OLD", dto.AnalysisCodeSnapshot);
        Assert.Equal("Historical", dto.AnalysisNameSnapshot);
        Assert.Equal("old-unit", dto.UnitSnapshot);
        Assert.Equal(Convert.ToBase64String(Version), dto.Version);
        Assert.Equal(0, dto.ReferenceLowerBoundSnapshot);
        Assert.Equal(2, dto.ReferenceUpperBoundSnapshot);
        Assert.Equal(request.Id, dto.LaboratoryRequestId);
    }

    private const string Connection = "Server=offline.invalid;Database=NoDatabaseUnitTest;Integrated Security=True;Connect Timeout=1";
    private static ClinisysDbContext Context(OpeningFailure guard, bool retry = false) => new(new DbContextOptionsBuilder<ClinisysDbContext>()
        .UseSqlServer(Connection, options => { if (retry) options.EnableRetryOnFailure(); }).AddInterceptors(guard).Options);
    private static AnalysisResult Result()
    {
        var result = new AnalysisResult(ItemId, 1, "old-synthetic-actor", new(1, 2), DateTimeOffset.UnixEpoch);
        Set(result, nameof(result.Version), Version.ToArray());
        return result;
    }
    private static void Set(object entity, string property, object value)
    {
        var inherited = entity.GetType().GetProperty(property)!;
        inherited.DeclaringType!.GetProperty(property)!.SetValue(entity, value);
    }
    private static object? Invoke(string name, params object?[] arguments)
    {
        try { return typeof(AnalysisResultStore).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private sealed class Actor(string value) : IAnalysisResultActorProvider
    {
        public int Calls { get; private set; }
        public string GetRequiredActor() { Calls++; return value; }
    }
    private sealed class FailingActor(Exception failure) : IAnalysisResultActorProvider
    { public string GetRequiredActor() => throw failure; }
    private sealed class ConnectionBlocked : InvalidOperationException { }
    private sealed class OpeningFailure(Exception failure) : DbConnectionInterceptor
    {
        public int Attempts { get; private set; }
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        { Attempts++; throw failure; }
    }
    private static SqlException SqlFailure(int number)
    {
        var constructor = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderBy(x => x.GetParameters().Length).First();
        var arguments = constructor.GetParameters().Select(p => p.ParameterType.IsValueType
            ? Activator.CreateInstance(p.ParameterType) : p.ParameterType == typeof(string) ? "Synthetic" : null).ToArray();
        arguments[0] = number;
        var error = (SqlError)constructor.Invoke(arguments);
        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(errors, [error]);
        return (SqlException)typeof(SqlException).GetMethod("CreateException", BindingFlags.NonPublic | BindingFlags.Static,
            binder: null, types: [typeof(SqlErrorCollection), typeof(string)], modifiers: null)!.Invoke(null, [errors, "synthetic"])!;
    }
}
