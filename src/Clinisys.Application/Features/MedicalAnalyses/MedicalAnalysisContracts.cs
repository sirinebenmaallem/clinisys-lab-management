using System.ComponentModel.DataAnnotations;
using Clinisys.Application.Common;
using Clinisys.Domain.Entities;

namespace Clinisys.Application.Features.MedicalAnalyses;

public sealed record AnalysisInput(
    [Required, StringLength(30)] string Code,
    [Required, StringLength(200)] string Name,
    [Required, StringLength(30)] string Unit,
    [Required] decimal? Price);

public sealed record AnalysisDto(Guid Id, string Code, string Name, string Unit, decimal Price, bool IsActive);

public interface IMedicalAnalysisStore
{
    Task<AnalysisDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<CatalogPage<AnalysisDto>> SearchAsync(CatalogPageQuery query, CancellationToken cancellationToken);
    Task<AnalysisDto> CreateAsync(MedicalAnalysis analysis, CancellationToken cancellationToken);
    Task<AnalysisDto> UpdateAsync(Guid id, MedicalAnalysis draft, CancellationToken cancellationToken);
    Task DeactivateAsync(Guid id, CancellationToken cancellationToken);
}
