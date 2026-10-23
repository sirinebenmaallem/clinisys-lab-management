using Clinisys.Domain.Common;

namespace Clinisys.Domain.Entities;

// Records which panels were selected, independently of their future catalogue composition.
public sealed class RequestPanel : Entity
{
    private RequestPanel() { }
    public RequestPanel(Guid laboratoryRequestId, AnalysisPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        LaboratoryRequestId = Guard.Id(laboratoryRequestId, nameof(laboratoryRequestId));
        AnalysisPanelId = panel.Id;
        PanelCodeSnapshot = panel.Code;
        PanelNameSnapshot = panel.Name;
    }

    public Guid LaboratoryRequestId { get; private set; }
    public Guid AnalysisPanelId { get; private set; }
    public string PanelCodeSnapshot { get; private set; } = null!;
    public string PanelNameSnapshot { get; private set; } = null!;
}
