using System.Data.Common;
using System.Text.Json;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Application.Features.LaboratoryReports;
using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Domain;
using Clinisys.Infrastructure.LaboratoryReports;
using Clinisys.Infrastructure.LaboratoryRequests;
using Clinisys.Tests.AnalysisResults;
using Clinisys.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PdfSharp.Pdf.IO;

namespace Clinisys.Tests.Reports;

// Existing owned fixture and restricted connections; no new test infrastructure.
[Collection(AnalysisResultSqlCollection.Name)]
public sealed class LaboratoryReportSqlTests
{
    [SqlServerFact]
    public async Task RealProjection_RejectsUnvalidatedStates_AndGeneratesOfficialPdfWithoutBusinessWrites()
    {
        await using var fixture = await AnalysisResultSqlFixture.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var db = SqlServerTestDatabase.CreateContext();
        db.Database.SetCommandTimeout(10);
        await fixture.AddRangeAsync();
        var results = AnalysisResultSqlFixture.Store(db, Actor.Id);
        await results.CreateAsync(fixture.Request.Id, fixture.Item.Id, 3, ct);
        await results.CreateAsync(fixture.Request.Id, fixture.EmptyItem.Id, 0, ct);
        await using var readDb = SqlServerTestDatabase.CreateContext(new ReadOnlyCommands());
        readDb.Database.SetCommandTimeout(10);
        var reportStore = new LaboratoryReportStore(readDb);
        var reports = new LaboratoryReportQueries(reportStore, new LaboratoryReportPdf("Laboratoire synthétique SQL"));
        var workflow = new RequestWorkflowStore(db, TimeProvider.System, new Actor());
        var version = (await db.LaboratoryRequests.AsNoTracking().SingleAsync(x => x.Id == fixture.Request.Id, ct)).Version;

        // Registered, InProgress and ResultsEntered must each reject official output.
        foreach (var action in new[] { RequestWorkflowAction.Start, RequestWorkflowAction.CompleteEntry, RequestWorkflowAction.Validate })
        {
            var error = await Assert.ThrowsAsync<LaboratoryRequestException>(() => reports.GetAsync(fixture.Request.Id, ct));
            Assert.Equal(LaboratoryRequestError.Conflict, error.Error);
            version = Convert.FromBase64String((await workflow.TransitionAsync(fixture.Request.Id, action, version, ct)).Version);
        }

        foreach (var expected in new[] { RequestStatus.Validated, RequestStatus.Delivered })
        {
            if (expected == RequestStatus.Delivered)
                version = Convert.FromBase64String((await workflow.TransitionAsync(fixture.Request.Id, RequestWorkflowAction.Deliver, version, ct)).Version);
            var before = await Snapshot();
            var data = (await reportStore.GetAsync(fixture.Request.Id, ct))!;
            Assert.Equal(expected, data.Status);
            Assert.Equal(fixture.Patient.FirstName, data.FirstName);
            Assert.Equal(fixture.Request.PatientDateOfBirthSnapshot, data.DateOfBirthSnapshot);
            Assert.Equal(Actor.Id, data.ValidatedBy);
            var abnormal = Assert.Single(data.Analyses, a => a.Code == fixture.Item.AnalysisCodeSnapshot);
            Assert.Equal(3, abnormal.Value);
            Assert.Equal("unit", abnormal.Unit);
            Assert.Equal(-2, abnormal.LowerBound);
            Assert.Equal(2, abnormal.UpperBound);
            Assert.Equal(Abnormality.High, abnormal.Abnormality);
            var file = await reports.GetAsync(fixture.Request.Id, ct);
            Assert.True(file.Content.Length > 1000);
            using var stream = new MemoryStream(file.Content);
            using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            Assert.True(pdf.PageCount > 0);
            Assert.Equal(before, await Snapshot());
            Assert.Empty(readDb.ChangeTracker.Entries());
        }
        Assert.Null(await reportStore.GetAsync(Guid.NewGuid(), ct));

        async Task<string> Snapshot()
        {
            var request = await db.LaboratoryRequests.AsNoTracking().SingleAsync(x => x.Id == fixture.Request.Id, ct);
            var ids = new[] { fixture.Item.Id, fixture.EmptyItem.Id };
            var stored = await db.AnalysisResults.AsNoTracking().Where(x => ids.Contains(x.RequestItemId)).OrderBy(x => x.Id).ToListAsync(ct);
            var audits = await db.AuditEntries.AsNoTracking().Where(x => x.LaboratoryRequestId == fixture.Request.Id).OrderBy(x => x.Id).ToListAsync(ct);
            return JsonSerializer.Serialize(new { request, Results = stored, Audits = audits });
        }
    }

    private sealed class Actor : IAnalysisResultActorProvider
    {
        internal const string Id = "c0000000-0000-0000-0000-000000000010";
        public string GetRequiredActor() => Id;
    }
    private sealed class ReadOnlyCommands : DbCommandInterceptor
    {
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result) => throw new InvalidOperationException("Report SQL must be read-only.");
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Report SQL must be read-only.");
    }
}
