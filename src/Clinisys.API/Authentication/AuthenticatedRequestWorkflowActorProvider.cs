using System.Security.Claims;
using Clinisys.Application.Features.Authentication;
using Clinisys.Application.Features.LaboratoryRequests;

namespace Clinisys.API.Authentication;

// Action-specific authorization remains enforced by the workflow endpoint policies.
public sealed class AuthenticatedRequestWorkflowActorProvider(IHttpContextAccessor accessor) : IRequestWorkflowActorProvider
{
    public string GetRequiredActor()
    {
        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true
            || !AuthenticationRoles.All.Any(user.IsInRole)
            || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id == Guid.Empty)
            throw new InvalidOperationException("Authenticated workflow actor is required.");
        return id.ToString();
    }
}
