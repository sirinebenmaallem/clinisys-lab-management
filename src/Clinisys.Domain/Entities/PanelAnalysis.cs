using Clinisys.Domain.Common;

namespace Clinisys.Domain.Entities;

public sealed class PanelAnalysis : Entity
{
    private PanelAnalysis() { }
    public PanelAnalysis(Guid analysisPanelId, Guid medicalAnalysisId)
    {
        AnalysisPanelId = Guard.Id(analysisPanelId, nameof(analysisPanelId));
        MedicalAnalysisId = Guard.Id(medicalAnalysisId, nameof(medicalAnalysisId));
    }

    public Guid AnalysisPanelId { get; private set; }
    public Guid MedicalAnalysisId { get; private set; }
}
