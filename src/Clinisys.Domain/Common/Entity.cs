namespace Clinisys.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public byte[] Version { get; private set; } = [];
}
