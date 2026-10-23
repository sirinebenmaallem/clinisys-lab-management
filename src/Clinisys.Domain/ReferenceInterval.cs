using Clinisys.Domain.Common;

namespace Clinisys.Domain;

// Numeric bounds are inclusive. This value object has no EF dependency.
public sealed record ReferenceInterval
{
    public decimal LowerBound { get; }
    public decimal UpperBound { get; }

    public ReferenceInterval(decimal lowerBound, decimal upperBound)
    {
        LowerBound = Guard.Measurement(lowerBound, nameof(lowerBound));
        UpperBound = Guard.Measurement(upperBound, nameof(upperBound));
        if (lowerBound > upperBound)
            throw new ArgumentException("The lower bound cannot exceed the upper bound.");
    }

    public Abnormality Evaluate(decimal value)
    {
        Guard.Measurement(value, nameof(value));
        return value < LowerBound ? Abnormality.Low : value > UpperBound ? Abnormality.High : Abnormality.Normal;
    }
}
