using System.Reflection;
using System.Text;
using Clinisys.Application.Features.LaboratoryReports;
using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Domain;
using Clinisys.Infrastructure.LaboratoryReports;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.IO;
using PdfSharp.Pdf.IO;

namespace Clinisys.Tests.Reports;

public sealed class LaboratoryReportTests
{
    internal static LaboratoryReportData Report(RequestStatus status = RequestStatus.Validated) =>
        new(Guid.NewGuid(), "LAB-2026-000001", status, Guid.NewGuid(), "Élise", "Synthétique",
            new(2000, 2, 29), Sex.Female, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddDays(1), "b0000000-0000-0000-0000-000000000010",
            [new("SYN-LOW", "Analyse basse", "mg/dL", 1.25m, 2, 5, Abnormality.Low,
                DateTimeOffset.UnixEpoch.AddDays(1), "synthetic-biologist", false),
             new("SYN-HIGH", "Analyse haute", "mmol/L", 9.123456m, 2, 5, Abnormality.High,
                DateTimeOffset.UnixEpoch.AddDays(1), "synthetic-biologist", true),
             new("SYN-NORMAL", "Analyse normale", "µmol/L", 2, 2, 5, Abnormality.Normal,
                DateTimeOffset.UnixEpoch.AddDays(1), "synthetic-biologist", false),
             new("SYN-NONE", "Sans référence", "unit", 0, null, null, Abnormality.NotEvaluated,
                DateTimeOffset.UnixEpoch.AddDays(1), "synthetic-biologist", false)]);

    [Theory]
    [InlineData(RequestStatus.Validated)]
    [InlineData(RequestStatus.Delivered)]
    public void GeneratesNonemptyReadableA4Pdf_WithoutChangingReport(RequestStatus status)
    {
        var report = Report(status);
        var before = System.Text.Json.JsonSerializer.Serialize(report);
        var content = new LaboratoryReportPdf("Laboratoire synthétique").Generate(report);
        Assert.True(content.Length > 1000);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(content, 0, 8));
        Assert.Contains("%%EOF", Encoding.ASCII.GetString(content[^40..]));
        using var stream = new MemoryStream(content);
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount > 0);
        foreach (var page in pdf.Pages)
        {
            Assert.InRange(page.Width.Point, 594, 596);
            Assert.InRange(page.Height.Point, 841, 843);
            Assert.True(page.Contents.Elements.Count > 0);
        }
        Assert.DoesNotContain(report.FirstName, pdf.Info.Title);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(report));
    }

    [Fact]
    public void RealLayoutContainsSnapshotsUnitsBoundsAbnormalityAndValidator()
    {
        var report = Report();
        var renderer = new LaboratoryReportPdf("Laboratoire synthétique");
        var document = (Document)typeof(LaboratoryReportPdf).GetMethod("CreateDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(renderer, [report])!;
        var ddl = DdlWriter.WriteToString(document);
        foreach (var required in new[] { "Laboratoire synthétique", report.Number, "Élise", "Synthétique",
                     "29/02/2000", "Féminin", "Analyse basse", "mg/dL", "mmol/L", "µmol/L", "1,25", "9,123456",
                     "2 à 5", "Non disponible", "BAS - hors normes", "HAUT - hors normes", "Dans les normes", "Non évalué",
                     report.ValidatedBy!, "calculé", "UTC" })
            Assert.Contains(required, ddl);
        Assert.DoesNotContain("NationalIdentity", ddl);
        Assert.DoesNotContain("Phone", ddl);
    }

    [Fact]
    public void LargeReportPaginatesWithRepeatedTableHeader()
    {
        var report = Report();
        report = report with { Analyses = Enumerable.Range(0, 160)
            .Select(i => report.Analyses[0] with { Code = "SYN-" + i, Name = "Analyse synthétique à description longue pour vérifier la pagination " + i }).ToArray() };
        var bytes = new LaboratoryReportPdf("Laboratoire synthétique").Generate(report);
        using var stream = new MemoryStream(bytes);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount > 2);
        Assert.All(document.Pages.Cast<PdfSharp.Pdf.PdfPage>(), page => Assert.True(page.Contents.Elements.Count > 0));
    }

    [Theory]
    [InlineData(RequestStatus.Registered)]
    [InlineData(RequestStatus.InProgress)]
    [InlineData(RequestStatus.ResultsEntered)]
    public async Task UnvalidatedRequests_RejectBeforeRendering(RequestStatus status)
    {
        var report = Report(status);
        var pdf = new PdfSpy();
        var error = await Assert.ThrowsAsync<LaboratoryRequestException>(() =>
            new LaboratoryReportQueries(new Store(report), pdf).GetAsync(report.RequestId, default));
        Assert.Equal(LaboratoryRequestError.Conflict, error.Error);
        Assert.Equal(0, pdf.Calls);
        Assert.Throws<LaboratoryRequestException>(() => new LaboratoryReportPdf("Synthetic").Generate(report));
    }

    [Theory]
    [InlineData("request-date")]
    [InlineData("request-actor")]
    [InlineData("no-items")]
    [InlineData("value")]
    [InlineData("result-date")]
    [InlineData("result-actor")]
    public void InconsistentOrIncompleteOfficialData_IsNeverPrinted(string reason)
    {
        var report = Report();
        report = reason switch
        {
            "request-date" => report with { ValidatedAt = null },
            "request-actor" => report with { ValidatedBy = " " },
            "no-items" => report with { Analyses = [] },
            "value" => report with { Analyses = [report.Analyses[0] with { Value = null }] },
            "result-date" => report with { Analyses = [report.Analyses[0] with { ValidatedAt = null }] },
            _ => report with { Analyses = [report.Analyses[0] with { ValidatedBy = null }] }
        };
        Assert.Throws<LaboratoryRequestException>(() => LaboratoryReportQueries.EnsureOfficial(report));
    }

    [Fact]
    public async Task MissingRequestAndInvalidId_DoNotRender_AndFilenameIsSafe()
    {
        var pdf = new PdfSpy();
        var report = Report() with { Number = "LAB-2026/../../\r\nX-Evil: yes" };
        var queries = new LaboratoryReportQueries(new Store(report), pdf);
        var file = await queries.GetAsync(report.RequestId, default);
        Assert.Matches("^compte-rendu-[A-Za-z0-9_-]+\\.pdf$", file.FileName);
        Assert.DoesNotContain("Élise", file.FileName);
        Assert.Equal(1, pdf.Calls);
        var absent = new LaboratoryReportQueries(new Store(null), pdf);
        Assert.Equal(LaboratoryRequestError.NotFound,
            (await Assert.ThrowsAsync<LaboratoryRequestException>(() => absent.GetAsync(Guid.NewGuid(), default))).Error);
        Assert.Equal(LaboratoryRequestError.Validation,
            (await Assert.ThrowsAsync<LaboratoryRequestException>(() => absent.GetAsync(Guid.Empty, default))).Error);
        Assert.Equal(1, pdf.Calls);
    }

    private sealed class Store(LaboratoryReportData? data) : ILaboratoryReportStore
    { public Task<LaboratoryReportData?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(data); }
    private sealed class PdfSpy : ILaboratoryReportPdf
    { public int Calls; public byte[] Generate(LaboratoryReportData report) { Calls++; return [1, 2, 3]; } }
}
