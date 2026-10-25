using Clinisys.Application.Features.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Clinisys.API.Authentication;

public static class AuthProblems
{
    public static Task WriteAsync(HttpContext context, int status)
    {
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        var problem = new ProblemDetails { Status = status, Title = status switch
        { 400 => "Authentication input or antiforgery token is invalid.", 401 => "Authentication failed or is required.",
          403 => "Access denied.", 429 => "Too many login attempts.", _ => "Authentication could not be completed." } };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        return context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }
}

public sealed class AuthenticationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && ct.IsCancellationRequested) return false;
        if (exception is AuthenticationException known)
            await AuthProblems.WriteAsync(context, known.Error == AuthenticationError.Validation ? 400 : 401);
        else if (context.Request.Path.StartsWithSegments("/api/auth"))
            await AuthProblems.WriteAsync(context, exception is System.Data.Common.DbException or Microsoft.EntityFrameworkCore.DbUpdateException ? 503 : 500);
        else return false;
        return true;
    }
}
