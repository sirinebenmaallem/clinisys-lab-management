using Clinisys.Application.Common;
using Clinisys.Domain.Entities;

namespace Clinisys.Application.Features.MedicalAnalyses;

public sealed class MedicalAnalysisCommands(IMedicalAnalysisStore store)
{
    public Task<AnalysisDto> CreateAsync(AnalysisInput input, CancellationToken ct) => store.CreateAsync(Draft(input), ct);
    public Task<AnalysisDto> UpdateAsync(Guid id, AnalysisInput input, CancellationToken ct) => store.UpdateAsync(id, Draft(input), ct);
    public Task DeactivateAsync(Guid id, CancellationToken ct) => store.DeactivateAsync(id, ct);

    private static MedicalAnalysis Draft(AnalysisInput input) => CatalogValidation.Domain(() =>
        new MedicalAnalysis(input.Code, input.Name, input.Unit,
            input.Price ?? throw new ArgumentException("Price is required.", "price")));
}
