using Clinisys.Application.Features.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Clinisys.API.Authentication;

public sealed class CsrfFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method)) return;
        if (!context.HttpContext.Request.IsHttps || !await antiforgery.IsRequestValidAsync(context.HttpContext))
        {
            var problem = new ProblemDetails { Status = 400, Title = "Antiforgery validation failed." };
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            context.Result = new BadRequestObjectResult(problem);
        }
    }
}

public sealed class AntiforgeryTokenProvider(IAntiforgery antiforgery, IHttpContextAccessor accessor,
    IOptions<AntiforgeryOptions> options) : IAntiforgeryTokenProvider
{
    public Task<AntiforgeryTokenDto> GetTokenAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var context = accessor.HttpContext ?? throw AuthenticationException.Unauthenticated();
        var tokens = antiforgery.GetAndStoreTokens(context);
        return Task.FromResult(new AntiforgeryTokenDto(tokens.RequestToken!, options.Value.HeaderName!));
    }
}
