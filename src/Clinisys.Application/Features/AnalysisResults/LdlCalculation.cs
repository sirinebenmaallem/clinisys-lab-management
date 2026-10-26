using Clinisys.Domain;
using Clinisys.Domain.Entities;

namespace Clinisys.Application.Features.AnalysisResults;

// Explicit server configuration. No fuzzy labels, fixed IDs or invented catalogue entries.
public sealed record LdlAnalysisCodes(string? TotalCholesterol = null, string? Hdl = null,
    string? Triglycerides = null, string? CalculatedLdl = null)
{
    public bool IsValid => new[] { TotalCholesterol, Hdl, Triglycerides, CalculatedLdl }
        .All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 30 && x == x.Trim().ToUpperInvariant())
        && new[] { TotalCholesterol, Hdl, Triglycerides, CalculatedLdl }.Distinct(StringComparer.Ordinal).Count() == 4;
    public bool IsRelevant(string code) => IsValid &&
        new[] { TotalCholesterol, Hdl, Triglycerides, CalculatedLdl }.Contains(code, StringComparer.Ordinal);
}

public sealed record LdlCalculationDto(string Status, Guid? RequestItemId, decimal? Value);

public static class LdlCalculation
{
    public static LdlCalculationDto Evaluate(LdlAnalysisCodes codes, IReadOnlyList<RequestItem> items,
        IReadOnlyList<AnalysisResult> results)
    {
        if (!codes.IsValid) return new("NotConfigured", null, null);
        RequestItem? Find(string? code) => items.SingleOrDefault(x => x.AnalysisCodeSnapshot == code);
        if (new[] { codes.TotalCholesterol, codes.Hdl, codes.Triglycerides, codes.CalculatedLdl }
            .Any(code => items.Count(i => i.AnalysisCodeSnapshot == code) > 1))
            return new("AmbiguousAnalysis", null, null);
        var target = Find(codes.CalculatedLdl);
        if (target is null) return new("LdlNotRequested", null, null);
        var existing = results.SingleOrDefault(r => r.RequestItemId == target.Id);
        if (existing is { IsCalculated: false }) return new("ManualResultPreserved", target.Id, null);
        var total = Find(codes.TotalCholesterol);
        var hdl = Find(codes.Hdl);
        var tg = Find(codes.Triglycerides);
        decimal? Value(RequestItem? item) => results.SingleOrDefault(r => r.RequestItemId == item?.Id)?.Value;
        var outcome = Friedewald.Calculate(Value(total), Value(hdl), Value(tg),
            total?.UnitSnapshot ?? "", hdl?.UnitSnapshot ?? "", tg?.UnitSnapshot ?? "", target.UnitSnapshot);
        return new(outcome.Status, target.Id, outcome.Value);
    }
}
