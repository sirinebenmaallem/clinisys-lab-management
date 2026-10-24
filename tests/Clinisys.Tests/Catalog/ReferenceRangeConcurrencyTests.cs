using Clinisys.Application.Common;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.Catalog;
using Clinisys.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Tests.Catalog;

public sealed class ReferenceRangeConcurrencyTests
{
    [SqlServerFact]
    public async Task ConcurrentOverlappingCreates_AllowOnlyOneRange()
    {
        var analysis = new MedicalAnalysis($"T{Guid.NewGuid():N}"[..25], "Synthetic concurrency test", "unit", 1);
        // Only synthetic catalogue rows are committed so two independent sessions can race.
        // No patients, requests, results or historical records are created or deleted here.
        await using (var seed = SqlServerTestDatabase.CreateContext())
        {
            seed.MedicalAnalyses.Add(analysis);
            await seed.SaveChangesAsync();
        }
        try
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<string> Create(int minimum, int maximum)
            {
                await using var db = SqlServerTestDatabase.CreateContext();
                await start.Task;
                try
                {
                    await new ReferenceRangeStore(db).CreateAsync(new ReferenceRange(analysis.Id, Sex.Female,
                        minimum, maximum, new ReferenceInterval(0, 1)), CancellationToken.None);
                    return "created";
                }
                catch (CatalogException ex) when (ex.Error == CatalogError.Conflict && ex.Message.Contains("overlapping"))
                {
                    return "overlap";
                }
            }
            var first = Create(0, 20);
            var second = Create(10, 30);
            start.SetResult();
            var outcomes = await Task.WhenAll(first, second);
            Assert.Equal(new[] { "created", "overlap" }, outcomes.Order());
            await using var verify = SqlServerTestDatabase.CreateContext();
            Assert.Equal(1, await verify.ReferenceRanges.CountAsync(x => x.MedicalAnalysisId == analysis.Id));
        }
        finally
        {
            await using var cleanup = SqlServerTestDatabase.CreateContext();
            cleanup.ReferenceRanges.RemoveRange(await cleanup.ReferenceRanges.Where(x => x.MedicalAnalysisId == analysis.Id).ToListAsync());
            cleanup.MedicalAnalyses.Remove(analysis);
            await cleanup.SaveChangesAsync();
        }
    }
}
