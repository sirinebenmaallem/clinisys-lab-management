using Clinisys.API.Errors;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Clinisys.API.AnalysisResults;

// Error routing only; authorization is enforced by endpoint policies.
public sealed class ResultErrorScopeFilter : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        context.HttpContext.Items[AnalysisResultExceptionHandler.RequestMarker] = true;
        await next();
    }
}
