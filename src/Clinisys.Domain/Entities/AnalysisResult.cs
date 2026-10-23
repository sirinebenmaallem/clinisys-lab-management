using Clinisys.Domain.Common;

namespace Clinisys.Domain.Entities;

public sealed class AnalysisResult : Entity
{
    private AnalysisResult() { }
    public AnalysisResult(Guid requestItemId, decimal value, string enteredBy, ReferenceInterval? interval = null)
    {
        RequestItemId = Guard.Id(requestItemId, nameof(requestItemId));
        Value = Guard.Measurement(value, nameof(value));
        EnteredBy = Guard.Required(enteredBy, 100, nameof(enteredBy));
        ReferenceLowerBoundSnapshot = interval?.LowerBound;
        ReferenceUpperBoundSnapshot = interval?.UpperBound;
        Abnormality = interval?.Evaluate(value) ?? Abnormality.NotEvaluated;
    }

    public Guid RequestItemId { get; private set; }
    public decimal Value { get; private set; }
    public decimal? ReferenceLowerBoundSnapshot { get; private set; }
    public decimal? ReferenceUpperBoundSnapshot { get; private set; }
    public Abnormality Abnormality { get; private set; }
    public DateTimeOffset EnteredAt { get; private set; } = DateTimeOffset.UtcNow;
    public string EnteredBy { get; private set; } = null!;
    public DateTimeOffset? ValidatedAt { get; private set; }
    public string? ValidatedBy { get; private set; }
}
