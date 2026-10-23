using Clinisys.Domain.Common;

namespace Clinisys.Domain.Entities;

public sealed class MedicalAnalysis : Entity
{
    private MedicalAnalysis() { }

    public MedicalAnalysis(string code, string name, string unit, decimal price)
    {
        Code = Guard.Required(code, 30, nameof(code)).ToUpperInvariant();
        Name = Guard.Required(name, 200, nameof(name));
        Unit = Guard.Required(unit, 30, nameof(unit));
        Price = Guard.Price(price, nameof(price));
    }

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Unit { get; private set; } = null!;
    public decimal Price { get; private set; }
    public bool IsActive { get; private set; } = true;
}
