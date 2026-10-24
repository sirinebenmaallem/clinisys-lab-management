using Clinisys.Domain.Common;

namespace Clinisys.Domain.Entities;

public sealed class AnalysisPanel : Entity
{
    private AnalysisPanel() { }
    public AnalysisPanel(string code, string name)
        => Update(code, name);

    public void Update(string code, string name)
    {
        var validCode = Guard.Required(code, 30, nameof(code)).ToUpperInvariant();
        var validName = Guard.Required(name, 200, nameof(name));
        (Code, Name) = (validCode, validName);
    }

    public void Deactivate() => IsActive = false;

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
}
