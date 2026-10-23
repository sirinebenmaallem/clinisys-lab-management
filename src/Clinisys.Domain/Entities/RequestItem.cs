using Clinisys.Domain.Common;

namespace Clinisys.Domain.Entities;

public sealed class RequestItem : Entity
{
    private RequestItem() { }
    public RequestItem(LaboratoryRequest request, MedicalAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(analysis);
        LaboratoryRequestId = request.Id;
        MedicalAnalysisId = analysis.Id;
        AnalysisCodeSnapshot = analysis.Code;
        AnalysisNameSnapshot = analysis.Name;
        UnitSnapshot = analysis.Unit;
        UnitPriceSnapshot = analysis.Price;
        DiscountPercentSnapshot = request.DiscountPercent;
    }

    public Guid LaboratoryRequestId { get; private set; }
    public Guid MedicalAnalysisId { get; private set; }
    public string AnalysisCodeSnapshot { get; private set; } = null!;
    public string AnalysisNameSnapshot { get; private set; } = null!;
    public string UnitSnapshot { get; private set; } = null!;
    public decimal UnitPriceSnapshot { get; private set; }
    public decimal DiscountPercentSnapshot { get; private set; }
    public decimal NetPrice => decimal.Round(UnitPriceSnapshot * (1 - DiscountPercentSnapshot / 100), 2, MidpointRounding.AwayFromZero);
}
