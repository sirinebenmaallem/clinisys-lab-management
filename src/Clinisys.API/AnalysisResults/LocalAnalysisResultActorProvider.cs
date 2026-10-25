using Clinisys.Application.Features.AnalysisResults;

namespace Clinisys.API.AnalysisResults;

public sealed class LocalAnalysisResultActorProvider(AnalysisResultLocalAccess access, IHttpContextAccessor accessor)
    : IAnalysisResultActorProvider
{
    public string GetRequiredActor()
    {
        if (!access.IsAllowed(accessor.HttpContext))
            throw new InvalidOperationException("Local result entry is not authorized in this context.");
        return "LOCAL-UNAUTHENTICATED";
    }
}
