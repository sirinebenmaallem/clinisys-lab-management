using Clinisys.Application.Common;
using Clinisys.Domain.Entities;

namespace Clinisys.Application.Features.AnalysisPanels;

public sealed class AnalysisPanelCommands(IAnalysisPanelStore store)
{
    public Task<PanelDto> CreateAsync(PanelInput input, CancellationToken ct) => store.CreateAsync(Draft(input), ct);
    public Task<PanelDto> UpdateAsync(Guid id, PanelInput input, CancellationToken ct) => store.UpdateAsync(id, Draft(input), ct);
    public Task DeactivateAsync(Guid id, CancellationToken ct) => store.DeactivateAsync(id, ct);
    public Task AddAnalysisAsync(Guid panelId, Guid analysisId, CancellationToken ct) => store.AddAnalysisAsync(panelId, analysisId, ct);
    public Task RemoveAnalysisAsync(Guid panelId, Guid analysisId, CancellationToken ct) => store.RemoveAnalysisAsync(panelId, analysisId, ct);
    private static AnalysisPanel Draft(PanelInput input) => CatalogValidation.Domain(() => new AnalysisPanel(input.Code, input.Name));
}
