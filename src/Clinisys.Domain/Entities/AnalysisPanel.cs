using Clinisys.Domain.Common;

namespace Clinisys.Domain.Entities;

public sealed class AnalysisPanel : Entity
{
    private AnalysisPanel() { }
    public AnalysisPanel(string code, string name)
    {
        Code = Guard.Required(code, 30, nameof(code)).ToUpperInvariant();
        Name = Guard.Required(name, 200, nameof(name));
    }

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
}
