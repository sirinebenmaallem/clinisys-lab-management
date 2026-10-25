using System.Security.Claims;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Application.Features.Authentication;

namespace Clinisys.API.AnalysisResults;

public sealed class AuthenticatedAnalysisResultActorProvider(IHttpContextAccessor accessor) : IAnalysisResultActorProvider
{
    public string GetRequiredActor()
    {
        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true
            || !(user.IsInRole(AuthenticationRoles.Technician) || user.IsInRole(AuthenticationRoles.Biologist))
            || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id == Guid.Empty)
            throw new InvalidOperationException("Authenticated result entry is required.");
        return id.ToString();
    }
}
