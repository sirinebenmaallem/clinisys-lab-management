using Clinisys.Domain.Common;

namespace Clinisys.Domain.Entities;

public sealed class MedicalAnalysis : Entity
{
    private MedicalAnalysis() { }

    public MedicalAnalysis(string code, string name, string unit, decimal price)
        => Update(code, name, unit, price);

    public void Update(string code, string name, string unit, decimal price)
    {
        var validCode = Guard.Required(code, 30, nameof(code)).ToUpperInvariant();
        var validName = Guard.Required(name, 200, nameof(name));
        var validUnit = Guard.Required(unit, 30, nameof(unit));
        var validPrice = Guard.Price(price, nameof(price));
        (Code, Name, Unit, Price) = (validCode, validName, validUnit, validPrice);
    }

    public void Deactivate() => IsActive = false;

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Unit { get; private set; } = null!;
    public decimal Price { get; private set; }
    public bool IsActive { get; private set; } = true;
}
