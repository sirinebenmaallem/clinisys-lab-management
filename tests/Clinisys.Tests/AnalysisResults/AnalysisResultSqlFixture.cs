using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.AnalysisResults;
using Clinisys.Infrastructure.Persistence;
using Clinisys.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Tests.AnalysisResults;

[CollectionDefinition(AnalysisResultSqlCollection.Name, DisableParallelization = true)]
public sealed class AnalysisResultSqlCollection
{
    public const string Name = "AnalysisResult SQL integration";
}

// Created ONLY inside opt-in SqlServerFact methods. All connections go through
// the existing restricted helper; no migration, counter allocation or API activation.
internal sealed class AnalysisResultSqlFixture : IAsyncDisposable
{
    internal static readonly DateTimeOffset EntryTime = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);
    internal Patient Patient { get; } = new("Synthetic", "Result SQL", new(2024, 2, 28), Sex.Female, phoneNumber: "TEST-ONLY");
    internal MedicalAnalysis Analysis { get; } = new(Code(), "Synthetic first", "unit", 15);
    internal MedicalAnalysis OtherAnalysis { get; } = new(Code(), "Synthetic second", "unit", 10);
    internal LaboratoryRequest Request { get; }
    internal LaboratoryRequest OtherRequest { get; }
    internal RequestItem Item { get; }
    internal RequestItem EmptyItem { get; }
    internal RequestItem ForeignItem { get; }
    private readonly List<Guid> rangeIds = [];

    private AnalysisResultSqlFixture()
    {
        // Synthetic legacy-format numbers isolate these tests from the Step 05
        // generator. No existing or new annual counter is ever touched.
        Request = new(Code(), Patient, "Synthetic physician");
        OtherRequest = new(Code(), Patient, "Synthetic physician");
        var registered = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero); // age = 2 calendar days
        typeof(LaboratoryRequest).GetProperty(nameof(LaboratoryRequest.RegisteredAt))!.SetValue(Request, registered);
        typeof(LaboratoryRequest).GetProperty(nameof(LaboratoryRequest.RegisteredAt))!.SetValue(OtherRequest, registered);
        Item = new(Request, Analysis);
        EmptyItem = new(Request, OtherAnalysis);
        ForeignItem = new(OtherRequest, Analysis);
    }

    internal static async Task<AnalysisResultSqlFixture> CreateAsync()
    {
        var fixture = new AnalysisResultSqlFixture();
        await using var db = SqlServerTestDatabase.CreateContext();
        db.AddRange(fixture.Patient, fixture.Analysis, fixture.OtherAnalysis, fixture.Request,
            fixture.OtherRequest, fixture.Item, fixture.EmptyItem, fixture.ForeignItem);
        await db.SaveChangesAsync();
        // Do not clean up after a failed seed: a failed insert/identifier collision
        // must NEVER authorize deleting any pre-existing row with that identifier.
        return fixture;
    }

    internal async Task<ReferenceRange> AddRangeAsync(Sex sex = Sex.Female, int minimum = 2,
        int? maximum = 3, decimal lower = -2, decimal upper = 2)
    {
        var range = new ReferenceRange(Analysis.Id, sex, minimum, maximum, new(lower, upper));
        await using var db = SqlServerTestDatabase.CreateContext();
        db.ReferenceRanges.Add(range);
        await db.SaveChangesAsync();
        rangeIds.Add(range.Id); // Ownership is recorded only after successful insertion.
        return range;
    }

    internal static AnalysisResultStore Store(ClinisysDbContext db, string actor = "SYN-ENTRY", DateTimeOffset? instant = null) =>
        new(db, new FixedClock(instant ?? EntryTime), new SyntheticActor(actor));

    public async ValueTask DisposeAsync()
    {
        // Independent deadline; failure never authorizes broader deletion or retries.
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cleanupTimeout.Token;
        await using var db = SqlServerTestDatabase.CreateContext();
        db.Database.SetCommandTimeout(10);
        await db.Database.OpenConnectionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 5000", ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var items = new[] { Item.Id, EmptyItem.Id, ForeignItem.Id };
        var requests = new[] { Request.Id, OtherRequest.Id };
        var analyses = new[] { Analysis.Id, OtherAnalysis.Id };
        await db.AnalysisResults.Where(x => items.Contains(x.RequestItemId)).ExecuteDeleteAsync(ct);
        await db.RequestItems.Where(x => items.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await db.LaboratoryRequests.Where(x => requests.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await db.ReferenceRanges.Where(x => rangeIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await db.MedicalAnalyses.Where(x => analyses.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await db.Patients.Where(x => x.Id == Patient.Id).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static string Code() => $"SYN{Guid.NewGuid():N}"[..30];
    private sealed class FixedClock(DateTimeOffset instant) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => instant; }
    private sealed class SyntheticActor(string actor) : IAnalysisResultActorProvider
    { public string GetRequiredActor() => actor; }
}
