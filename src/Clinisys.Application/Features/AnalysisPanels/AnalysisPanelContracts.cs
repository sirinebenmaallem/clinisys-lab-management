using System.ComponentModel.DataAnnotations;
using Clinisys.Application.Common;
using Clinisys.Application.Features.MedicalAnalyses;
using Clinisys.Domain.Entities;

namespace Clinisys.Application.Features.AnalysisPanels;

public sealed record PanelInput(
    [Required, StringLength(30)] string Code,
    [Required, StringLength(200)] string Name);
public sealed record PanelDto(Guid Id, string Code, string Name, bool IsActive);
public sealed record PanelDetailsDto(Guid Id, string Code, string Name, bool IsActive, IReadOnlyList<AnalysisDto> Analyses);

public interface IAnalysisPanelStore
{
    Task<PanelDetailsDto?> GetAsync(Guid id, CancellationToken ct);
    Task<CatalogPage<PanelDto>> SearchAsync(CatalogPageQuery query, CancellationToken ct);
    Task<PanelDto> CreateAsync(AnalysisPanel panel, CancellationToken ct);
    Task<PanelDto> UpdateAsync(Guid id, AnalysisPanel draft, CancellationToken ct);
    Task DeactivateAsync(Guid id, CancellationToken ct);
    Task AddAnalysisAsync(Guid panelId, Guid analysisId, CancellationToken ct);
    Task RemoveAnalysisAsync(Guid panelId, Guid analysisId, CancellationToken ct);
}
