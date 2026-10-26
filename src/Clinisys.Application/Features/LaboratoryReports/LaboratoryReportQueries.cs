using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Domain;

namespace Clinisys.Application.Features.LaboratoryReports;

public sealed record ReportAnalysisDto(string Code, string Name, string Unit, decimal? Value,
    decimal? LowerBound, decimal? UpperBound, Abnormality? Abnormality,
    DateTimeOffset? ValidatedAt, string? ValidatedBy, bool IsCalculated);

public sealed record LaboratoryReportData(Guid RequestId, string Number, RequestStatus Status,
    Guid PatientId, string FirstName, string LastName, DateOnly DateOfBirthSnapshot, Sex SexSnapshot,
    DateTimeOffset RegisteredAt, DateTimeOffset? ValidatedAt, string? ValidatedBy,
    IReadOnlyList<ReportAnalysisDto> Analyses);

public sealed record LaboratoryReportFile(byte[] Content, string FileName);

public interface ILaboratoryReportStore
{
    Task<LaboratoryReportData?> GetAsync(Guid requestId, CancellationToken ct);
}

public interface ILaboratoryReportPdf
{
    byte[] Generate(LaboratoryReportData report);
}

public sealed class LaboratoryReportQueries(ILaboratoryReportStore store, ILaboratoryReportPdf pdf)
{
    public async Task<LaboratoryReportFile> GetAsync(Guid requestId, CancellationToken ct)
    {
        if (requestId == Guid.Empty) throw LaboratoryRequestException.Validation("request");
        var report = await store.GetAsync(requestId, ct) ?? throw LaboratoryRequestException.NotFound();
        EnsureOfficial(report);
        ct.ThrowIfCancellationRequested();
        var content = pdf.Generate(report);
        var safeNumber = new string(report.Number.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').Take(40).ToArray());
        if (safeNumber.Length == 0) safeNumber = report.RequestId.ToString("N");
        return new(content, $"compte-rendu-{safeNumber}.pdf");
    }

    public static void EnsureOfficial(LaboratoryReportData report)
    {
        if (report.Status is not (RequestStatus.Validated or RequestStatus.Delivered) ||
            report.ValidatedAt is null || string.IsNullOrWhiteSpace(report.ValidatedBy) ||
            report.Analyses.Count == 0 || report.Analyses.Any(a => a.Value is null ||
                a.ValidatedAt is null || string.IsNullOrWhiteSpace(a.ValidatedBy)))
            throw new LaboratoryRequestException(LaboratoryRequestError.Conflict,
                "An official report requires a validated request with complete validated results.");
    }
}
