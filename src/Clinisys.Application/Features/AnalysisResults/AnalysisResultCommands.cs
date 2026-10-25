namespace Clinisys.Application.Features.AnalysisResults;

public sealed class AnalysisResultCommands(IAnalysisResultStore store)
{
    public Task<AnalysisResultDetailDto> CreateAsync(Guid requestId, Guid itemId, AnalysisResultInput input, CancellationToken ct)
    {
        ValidateIds(requestId, itemId);
        var value = ValidateValue(input?.Value);
        return store.CreateAsync(requestId, itemId, value, ct);
    }

    public Task<AnalysisResultDetailDto> UpdateAsync(Guid requestId, Guid itemId, AnalysisResultUpdateInput input, CancellationToken ct)
    {
        ValidateIds(requestId, itemId);
        var value = ValidateValue(input?.Value);
        byte[] version;
        try { version = Convert.FromBase64String(input?.Version ?? ""); }
        catch (FormatException) { throw AnalysisResultException.Validation("version"); }
        if (version.Length != 8) throw AnalysisResultException.Validation("version");
        return store.UpdateAsync(requestId, itemId, value, version, ct);
    }

    private static decimal ValidateValue(decimal? value)
    {
        if (value is null || value < -999999999999.999999m || value > 999999999999.999999m
            || decimal.Round(value.Value, 6) != value.Value)
            throw AnalysisResultException.Validation("value");
        return value.Value;
    }

    private static void ValidateIds(Guid requestId, Guid itemId)
    {
        if (requestId == Guid.Empty || itemId == Guid.Empty) throw AnalysisResultException.Validation("identifiers");
    }
}
