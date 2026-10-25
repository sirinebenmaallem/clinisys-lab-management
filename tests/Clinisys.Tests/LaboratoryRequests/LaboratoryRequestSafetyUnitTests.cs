using System.Data.Common;
using System.Reflection;
using System.Transactions;
using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Infrastructure.LaboratoryRequests;
using Clinisys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Clinisys.API.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clinisys.Tests.LaboratoryRequests;

public sealed class LaboratoryRequestSafetyUnitTests
{
    [Theory]
    [InlineData(1205, false, 409)]
    [InlineData(1205, true, 409)]
    [InlineData(547, false, 503)]
    [InlineData(547, true, 500)]
    [InlineData(2627, true, 500)]
    [InlineData(-2, true, 500)]
    [InlineData(0, false, 500)]
    public async Task Reads_NormalizeOnlyDeadlocks_WithoutOpeningSqlOrRetrying(int number, bool wrapped, int status)
    {
        Exception failure = number == 0 ? new InvalidOperationException("Synthetic 1205 message") : SqlFailure(number);
        if (wrapped) failure = new InvalidOperationException("Synthetic EF envelope", new DbUpdateException("Synthetic", failure));
        foreach (var list in new[] { false, true })
        {
            var guard = new FailReadOpening(failure);
            await using var db = new ClinisysDbContext(new DbContextOptionsBuilder<ClinisysDbContext>()
                .UseSqlServer("Server=offline.invalid;Database=NoDatabaseUnitTest;Integrated Security=True;Connect Timeout=1")
                .AddInterceptors(guard).Options);
            var store = new LaboratoryRequestStore(db, TimeProvider.System);
            var error = await Record.ExceptionAsync(async () =>
            {
                if (list) await store.SearchAsync(new(), default);
                else await store.GetAsync(Guid.NewGuid(), default);
            });
            Assert.Equal(1, guard.Attempts);
            if (number == 1205) Assert.Equal(LaboratoryRequestError.Conflict, Assert.IsType<LaboratoryRequestException>(error).Error);
            else Assert.Same(failure, error);

            var http = new DefaultHttpContext();
            http.Response.Body = new MemoryStream();
            var handled = await new LaboratoryRequestExceptionHandler(NullLogger<LaboratoryRequestExceptionHandler>.Instance)
                .TryHandleAsync(http, error!, default);
            if (!handled) await new CatalogExceptionHandler(NullLogger<CatalogExceptionHandler>.Instance)
                .TryHandleAsync(http, error!, default);
            Assert.Equal(status, http.Response.StatusCode);
            Assert.Contains("application/problem+json", http.Response.ContentType!);
        }
    }

    private sealed class FailReadOpening(Exception failure) : DbConnectionInterceptor
    {
        public int Attempts { get; private set; }
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw failure; // Before SqlConnection.Open: no physical connection is attempted.
        }
    }

    [Theory]
    [InlineData(1205)]
    [InlineData(547)]
    [InlineData(2601)]
    [InlineData(2627)]
    [InlineData(51021)]
    [InlineData(-2)]
    public void SqlNumber_PreservesCodesThroughEfWrappers(int number)
    {
        var sql = SqlFailure(number);
        foreach (var failure in new Exception[] { sql, new DbUpdateException("Synthetic", sql),
            new InvalidOperationException("Synthetic", sql),
            new InvalidOperationException("Synthetic", new DbUpdateException("Synthetic", sql)) })
            Assert.Equal(number, Detect(failure));
    }

    [Fact]
    public void SqlNumber_DoesNotInferDeadlockFromMessagesOrUnknownOutcomes()
    {
        Assert.Null(Detect(new InvalidOperationException("SQL 1205 deadlock")));
        Assert.Null(Detect(new InvalidOperationException("Synthetic", new Exception("1205"))));
        Assert.Null(Detect(new Exception("Unrecognized envelope", SqlFailure(1205))));
        Assert.Null(Detect(LaboratoryRequestException.Unknown()));
        Assert.Equal(LaboratoryRequestError.CommitUnknown, LaboratoryRequestException.Unknown().Error);
        Assert.NotEqual(1205, Detect(new InvalidOperationException("deadlock", SqlFailure(547))));
    }

    [Theory]
    [InlineData(1205, LaboratoryRequestError.Conflict)]
    [InlineData(547, LaboratoryRequestError.Conflict)]
    [InlineData(2601, LaboratoryRequestError.Internal)]
    [InlineData(2627, LaboratoryRequestError.Internal)]
    [InlineData(51021, LaboratoryRequestError.Conflict)]
    public void Translate_PreservesExistingClassificationsThroughEfWrappers(int number, LaboratoryRequestError expected)
    {
        var failure = new InvalidOperationException("Synthetic", new DbUpdateException("Synthetic", SqlFailure(number)));
        var translated = typeof(LaboratoryRequestStore).GetMethod("Translate", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [failure]);
        Assert.Equal(expected, Assert.IsType<LaboratoryRequestException>(translated).Error);
    }

    private static int? Detect(Exception exception) => (int?)typeof(LaboratoryRequestStore)
        .GetMethod("SqlNumber", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [exception]);

    // SqlClient exposes no public error constructors. Build only synthetic errors;
    // no connection, context, or SQL command is involved in these classification tests.
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
            binder: null, types: [typeof(SqlErrorCollection), typeof(string)], modifiers: null)!.Invoke(null, [errors, "synthetic"] )!;
    }

    [Fact]
    public async Task AmbientScope_IsRejectedBeforeAnyConnectionIsOpened()
    {
        var guard = new NeverOpen();
        await using var context = Context(guard);
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LaboratoryRequestStore(context,TimeProvider.System)
            .CreateAsync(new(Guid.NewGuid(),"Synthetic",AnalysisIds:[Guid.NewGuid()]),default));
        Assert.Equal(0,guard.Attempts);
    }

    [Fact]
    public async Task OwnedContextCreation_PreservesInterceptorsWithoutConfigurationFallback()
    {
        var guard = new NeverOpen();
        await using var context = Context(guard);
        // ConnectionOpening is intercepted before SqlConnection.Open: no server is contacted.
        await Assert.ThrowsAsync<ConnectionBlocked>(() => new LaboratoryRequestStore(context,TimeProvider.System)
            .CreateAsync(new(Guid.NewGuid(),"Synthetic",AnalysisIds:[Guid.NewGuid()]),default));
        Assert.Equal(1,guard.Attempts);
    }

    private static ClinisysDbContext Context(NeverOpen interceptor) => new(new DbContextOptionsBuilder<ClinisysDbContext>()
        .UseSqlServer("Server=offline.invalid;Database=NoDatabaseUnitTest;Integrated Security=True;Connect Timeout=1")
        .AddInterceptors(interceptor).Options);
    private sealed class ConnectionBlocked : InvalidOperationException { }
    private sealed class NeverOpen : DbConnectionInterceptor
    {
        public int Attempts { get; private set; }
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,ConnectionEventData eventData,
            InterceptionResult result,CancellationToken cancellationToken=default)
        { Attempts++; throw new ConnectionBlocked(); }
    }
}
