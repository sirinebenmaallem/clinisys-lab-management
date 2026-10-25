namespace Clinisys.Infrastructure.Persistence;

// Technical state; not a Domain entity and not exposed through the API.
public sealed class LaboratoryRequestCounter
{
    public int Year { get; private set; }
    public int LastValue { get; private set; }
}
