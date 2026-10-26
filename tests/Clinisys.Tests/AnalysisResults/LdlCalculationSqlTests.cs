using System.Data;
using System.Globalization;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Infrastructure.AnalysisResults;
using Clinisys.Infrastructure.LaboratoryRequests;
using Clinisys.Infrastructure.Persistence;
using Clinisys.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Clinisys.Tests.AnalysisResults;

// Existing restricted helper only. All fixture data/results/audits roll back.
// Fixed TEST codes are local to these tests and never configured in production.
[Collection(AnalysisResultSqlCollection.Name)]
public sealed class LdlCalculationSqlTests
{
    [SqlServerFact]
    public async Task MgDl_PersistsRecalculatesAuditsAndLocksAfterBiologicalValidation()
    {
        await using var session = await Session.Open();
        var first = await session.Results.CreateAsync(session.Request.Id, session.Items[0].Id, 200, session.Ct);
        Assert.Equal("MissingSource", first.LdlCalculation!.Status);
        await session.Results.CreateAsync(session.Request.Id, session.Items[1].Id, 50, session.Ct);
        var completed = await session.Results.CreateAsync(session.Request.Id, session.Items[2].Id, 150, session.Ct);
        Assert.Equal("Calculated", completed.LdlCalculation!.Status);
        var ldl = await session.Ldl();
        Assert.True(ldl.IsCalculated);
        Assert.Equal(120, ldl.Value);
        var initialVersion = ldl.Version.ToArray();
        var originalReferences = (ldl.ReferenceLowerBoundSnapshot, ldl.ReferenceUpperBoundSnapshot);
        var manualEdit = await Assert.ThrowsAsync<AnalysisResultException>(() => session.Results.UpdateAsync(
            session.Request.Id, session.Items[3].Id, 999, ldl.Version, session.Ct));
        Assert.Equal(AnalysisResultError.Conflict, manualEdit.Error);

        var total = (await session.Results.GetAsync(session.Request.Id, session.Items[0].Id, session.Ct))!;
        await session.Results.UpdateAsync(session.Request.Id, session.Items[0].Id, 220,
            Convert.FromBase64String(total.Version), session.Ct);
        ldl = await session.Ldl();
        Assert.Equal(140, ldl.Value);
        Assert.True(ldl.IsCalculated);
        Assert.False(initialVersion.SequenceEqual(ldl.Version));
        Assert.Equal(originalReferences, (ldl.ReferenceLowerBoundSnapshot, ldl.ReferenceUpperBoundSnapshot));
        var correction = Assert.Single(await session.Db.AuditEntries.AsNoTracking().Where(x =>
            x.LaboratoryRequestId == session.Request.Id && x.RequestItemId == session.Items[3].Id &&
            x.Action == AuditAction.Updated).ToListAsync(session.Ct));
        Assert.Equal(120, decimal.Parse(correction.OldValue!, CultureInfo.InvariantCulture));
        Assert.Equal(140, decimal.Parse(correction.NewValue!, CultureInfo.InvariantCulture));
        Assert.Equal(Session.ActorId, correction.Actor);
        var read = await session.Results.ListForRequestAsync(session.Request.Id, session.Ct);
        Assert.Equal(140, read!.LdlCalculation!.Value);
        Assert.True(Assert.Single(read.Items, x => x.RequestItemId == session.Items[3].Id).Result!.IsCalculated);

        var workflow = new RequestWorkflowStore(session.Db, TimeProvider.System, new Actor());
        var version = (await session.Db.LaboratoryRequests.AsNoTracking().SingleAsync(x => x.Id == session.Request.Id, session.Ct)).Version;
        foreach (var action in new[] { RequestWorkflowAction.Start, RequestWorkflowAction.CompleteEntry, RequestWorkflowAction.Validate })
            version = Convert.FromBase64String((await workflow.TransitionAsync(session.Request.Id, action, version, session.Ct)).Version);
        var auditCount = await session.Db.AuditEntries.CountAsync(x => x.LaboratoryRequestId == session.Request.Id, session.Ct);
        total = (await session.Results.GetAsync(session.Request.Id, session.Items[0].Id, session.Ct))!;
        var blocked = await Assert.ThrowsAsync<AnalysisResultException>(() => session.Results.UpdateAsync(
            session.Request.Id, session.Items[0].Id, 250, Convert.FromBase64String(total.Version), session.Ct));
        Assert.Equal(AnalysisResultError.Conflict, blocked.Error);
        Assert.Equal(220, (await session.Results.GetAsync(session.Request.Id, session.Items[0].Id, session.Ct))!.Value);
        ldl = await session.Ldl();
        Assert.Equal(140, ldl.Value);
        Assert.NotNull(ldl.ValidatedAt);
        Assert.Equal(auditCount, await session.Db.AuditEntries.CountAsync(x => x.LaboratoryRequestId == session.Request.Id, session.Ct));
    }

    [SqlServerFact]
    public async Task MmolL_UsesCorrectDivisorAndRejectsThreshold()
    {
        await using var session = await Session.Open("mmol/L");
        await session.Enter(5, 1, 2.2m);
        Assert.Equal(3, (await session.Ldl()).Value);
        var tg = (await session.Results.GetAsync(session.Request.Id, session.Items[2].Id, session.Ct))!;
        var updated = await session.Results.UpdateAsync(session.Request.Id, session.Items[2].Id, 4.5m,
            Convert.FromBase64String(tg.Version), session.Ct);
        Assert.Equal("TriglyceridesTooHigh", updated.LdlCalculation!.Status);
        Assert.Null(updated.LdlCalculation.Value);
        Assert.False(await session.Db.AnalysisResults.AnyAsync(x => x.RequestItemId == session.Items[3].Id, session.Ct));
    }

    [SqlServerFact]
    public async Task InvalidatedMgDl_RemovesOnlyAutomaticValueWithAudit_ThenCanCalculateAgain()
    {
        await using var session = await Session.Open();
        await session.Enter(200, 50, 150);
        var tg = (await session.Results.GetAsync(session.Request.Id, session.Items[2].Id, session.Ct))!;
        var invalid = await session.Results.UpdateAsync(session.Request.Id, session.Items[2].Id, 400,
            Convert.FromBase64String(tg.Version), session.Ct);
        Assert.Equal("TriglyceridesTooHigh", invalid.LdlCalculation!.Status);
        Assert.Null(await session.Results.GetAsync(session.Request.Id, session.Items[3].Id, session.Ct));
        var audit = Assert.Single(await session.Db.AuditEntries.AsNoTracking().Where(x => x.LaboratoryRequestId == session.Request.Id &&
            x.RequestItemId == session.Items[3].Id && x.NewValue == null).ToListAsync(session.Ct));
        Assert.Equal(120, decimal.Parse(audit.OldValue!, CultureInfo.InvariantCulture));
        Assert.Equal(3, await session.Db.AnalysisResults.CountAsync(x => session.ItemIds.Contains(x.RequestItemId), session.Ct));
        await session.Results.UpdateAsync(session.Request.Id, session.Items[2].Id, 150,
            Convert.FromBase64String(invalid.Version), session.Ct);
        Assert.Equal(120, (await session.Ldl()).Value);
    }

    [SqlServerFact]
    public async Task MixedOrUnknownUnits_SaveSourcesWithoutInventingLdl()
    {
        foreach (var unit in new[] { "mmol/L", "unknown" })
        {
            await using var session = await Session.Open("mg/dL", unit);
            var last = await session.Enter(200, 50, 150);
            Assert.Equal("IncompatibleUnits", last.LdlCalculation!.Status);
            Assert.Null(last.LdlCalculation.Value);
            Assert.Null(await session.Results.GetAsync(session.Request.Id, session.Items[3].Id, session.Ct));
            Assert.Equal(3, await session.Db.AnalysisResults.CountAsync(x => session.ItemIds.Contains(x.RequestItemId), session.Ct));
        }
    }

    [SqlServerFact]
    public async Task ManualLdl_IsNeverOverwrittenByAutomaticCalculation()
    {
        await using var session = await Session.Open();
        await session.Results.CreateAsync(session.Request.Id, session.Items[3].Id, 99, session.Ct);
        var last = await session.Enter(200, 50, 150);
        Assert.Equal("ManualResultPreserved", last.LdlCalculation!.Status);
        var ldl = await session.Ldl();
        Assert.False(ldl.IsCalculated);
        Assert.Equal(99, ldl.Value);
        Assert.Single(await session.Db.AuditEntries.Where(x => x.RequestItemId == session.Items[3].Id).ToListAsync(session.Ct));
    }

    private sealed class Actor : IAnalysisResultActorProvider, IRequestWorkflowActorProvider { public string GetRequiredActor() => Session.ActorId; }

    private sealed class Session : IAsyncDisposable
    {
        internal const string ActorId = "b0000000-0000-0000-0000-000000000009";
        private static readonly string[] Codes = ["TEST-CHOL-TOTAL", "TEST-HDL", "TEST-TRIG", "TEST-LDL-CALC"];
        private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        private IDbContextTransaction transaction = null!;
        internal ClinisysDbContext Db { get; } = SqlServerTestDatabase.CreateContext();
        internal LaboratoryRequest Request { get; private set; } = null!;
        internal RequestItem[] Items { get; private set; } = [];
        internal Guid[] ItemIds => Items.Select(x => x.Id).ToArray();
        internal CancellationToken Ct => timeout.Token;
        internal AnalysisResultStore Results => new(Db, TimeProvider.System, new Actor(),
            new(Codes[0], Codes[1], Codes[2], Codes[3]));

        internal static async Task<Session> Open(string unit = "mg/dL", string? hdlUnit = null)
        {
            var session = new Session();
            try
            {
                session.Db.Database.SetCommandTimeout(10);
                session.transaction = await session.Db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, session.Ct);
                if (await session.Db.MedicalAnalyses.AnyAsync(x => Codes.Contains(x.Code), session.Ct))
                    throw new InvalidOperationException("Synthetic TEST code already exists; no existing analysis may be reused or changed.");
                var patient = new Patient("Synthetic", "LDL SQL", new(2000, 1, 1), Sex.Unspecified, phoneNumber: "TEST-ONLY");
                var analyses = Codes.Select((code, i) => new MedicalAnalysis(code, "Synthetic LDL test", i == 1 ? hdlUnit ?? unit : unit, 1)).ToArray();
                session.Request = new LaboratoryRequest("SYN" + Guid.NewGuid().ToString("N")[..20], patient, "Synthetic");
                session.Items = analyses.Select(a => new RequestItem(session.Request, a)).ToArray();
                session.Db.Add(patient); session.Db.AddRange(analyses);
                session.Db.Add(session.Request); session.Db.AddRange(session.Items);
                await session.Db.SaveChangesAsync(session.Ct);
                return session;
            }
            catch { await session.DisposeAsync(); throw; }
        }

        internal async Task<AnalysisResultDetailDto> Enter(decimal total, decimal hdl, decimal tg)
        {
            await Results.CreateAsync(Request.Id, Items[0].Id, total, Ct);
            await Results.CreateAsync(Request.Id, Items[1].Id, hdl, Ct);
            return await Results.CreateAsync(Request.Id, Items[2].Id, tg, Ct);
        }
        internal Task<AnalysisResult> Ldl() => Db.AnalysisResults.AsNoTracking().SingleAsync(x => x.RequestItemId == Items[3].Id, Ct);
        public async ValueTask DisposeAsync()
        {
            try { if (transaction is not null) await transaction.DisposeAsync(); }
            finally { try { await Db.DisposeAsync(); } finally { timeout.Dispose(); } }
        }
    }
}
