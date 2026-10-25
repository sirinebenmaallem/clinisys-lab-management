using Clinisys.Domain.Common;

namespace Clinisys.Domain.Entities;

public sealed class AnalysisResult : Entity
{
    private AnalysisResult() { }
    public AnalysisResult(Guid requestItemId, decimal value, string enteredBy, ReferenceInterval? interval = null)
        : this(requestItemId, value, enteredBy, interval, DateTimeOffset.UtcNow) { }

    public AnalysisResult(Guid requestItemId, decimal value, string enteredBy, ReferenceInterval? interval,
        DateTimeOffset enteredAt)
    {
        RequireUtc(enteredAt);
        RequestItemId = Guard.Id(requestItemId, nameof(requestItemId));
        Value = Guard.Measurement(value, nameof(value));
        EnteredBy = Guard.Required(enteredBy, 100, nameof(enteredBy));
        ReferenceLowerBoundSnapshot = interval?.LowerBound;
        ReferenceUpperBoundSnapshot = interval?.UpperBound;
        Abnormality = interval?.Evaluate(value) ?? Abnormality.NotEvaluated;
        EnteredAt = enteredAt;
    }

    public void UpdateValue(decimal value, string enteredBy, DateTimeOffset enteredAt)
    {
        if (ValidatedAt is not null || ValidatedBy is not null)
            throw new InvalidOperationException("A validated result cannot be corrected.");

        var validValue = Guard.Measurement(value, nameof(value));
        var validActor = Guard.Required(enteredBy, 100, nameof(enteredBy));
        RequireUtc(enteredAt);
        var abnormality = ReferenceLowerBoundSnapshot is { } lower && ReferenceUpperBoundSnapshot is { } upper
            ? new ReferenceInterval(lower, upper).Evaluate(validValue)
            : Abnormality.NotEvaluated;

        // All preconditions and calculations succeed before any state is changed.
        Value = validValue;
        Abnormality = abnormality;
        EnteredBy = validActor;
        EnteredAt = enteredAt;
    }

    private static void RequireUtc(DateTimeOffset enteredAt)
    {
        if (enteredAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("The entry instant must be UTC.", nameof(enteredAt));
    }

    public Guid RequestItemId { get; private set; }
    public decimal Value { get; private set; }
    public decimal? ReferenceLowerBoundSnapshot { get; private set; }
    public decimal? ReferenceUpperBoundSnapshot { get; private set; }
    public Abnormality Abnormality { get; private set; }
    public DateTimeOffset EnteredAt { get; private set; }
    public string EnteredBy { get; private set; } = null!;
    public DateTimeOffset? ValidatedAt { get; private set; }
    public string? ValidatedBy { get; private set; }
}
