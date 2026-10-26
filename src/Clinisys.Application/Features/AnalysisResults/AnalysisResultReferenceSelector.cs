using Clinisys.Domain;
using Clinisys.Domain.Entities;

namespace Clinisys.Application.Features.AnalysisResults;

// Pure policy for FIRST entry only. The store supplies a consistent, locked
// catalogue view; this policy itself provides no SQL concurrency guarantees.
public static class AnalysisResultReferenceSelector
{
    public static AnalysisResultReferenceDto Preview(LaboratoryRequest request, RequestItem item,
        MedicalAnalysis analysis, IEnumerable<ReferenceRange> ranges, AnalysisResult? result = null)
    {
        if (result is not null)
            return Describe(result.ReferenceLowerBoundSnapshot, result.ReferenceUpperBoundSnapshot);
        try
        {
            var interval = Select(request, item, analysis, ranges);
            return Describe(interval?.LowerBound, interval?.UpperBound);
        }
        catch (AnalysisResultException ex) when (ex.Error == AnalysisResultError.Conflict)
        {
            // A read-only preview must not prevent viewing other results. Saving still
            // runs the original selector under locks and rejects incompatible data.
            return new("Unavailable");
        }
    }

    private static AnalysisResultReferenceDto Describe(decimal? lower, decimal? upper) =>
        lower is not null && upper is not null
            ? new("Applicable", lower.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                upper.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : new("NoReference");

    public static ReferenceInterval? Select(LaboratoryRequest request, RequestItem item,
        MedicalAnalysis analysis, IEnumerable<ReferenceRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(ranges);
        if (item.LaboratoryRequestId != request.Id || item.MedicalAnalysisId != analysis.Id)
            throw AnalysisResultException.Internal();

        var birthDate = request.PatientDateOfBirthSnapshot;
        var ageDays = DateOnly.FromDateTime(request.RegisteredAt.UtcDateTime).DayNumber - birthDate.DayNumber;
        if (birthDate == default || ageDays < 0 || !Enum.IsDefined(request.PatientSexSnapshot))
            throw AnalysisResultException.Internal();
        var applicable = ranges.Where(x => x.MedicalAnalysisId == item.MedicalAnalysisId
            && x.AppliesTo(ageDays, request.PatientSexSnapshot)).Take(2).ToArray();
        if (applicable.Length == 0) return null;
        if (applicable.Length > 1) throw AnalysisResultException.AmbiguousReference();
        if (!string.Equals(item.UnitSnapshot, analysis.Unit, StringComparison.Ordinal))
            throw AnalysisResultException.IncompatibleUnit();
        return new ReferenceInterval(applicable[0].LowerBound, applicable[0].UpperBound);
    }
}
