using System.Data.Common;
using Clinisys.Application.Features.Dashboard;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Clinisys.API.Errors;

public sealed class DashboardExceptionHandler(ILogger<DashboardExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (!context.Request.Path.StartsWithSegments("/api/dashboard")) return false;
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested) return false;
        var status = exception switch { DashboardException => 400, SqlException { Number: 1205 } => 409, DbException => 503, _ => 500 };
        ProblemDetails problem = status == 400
            ? new ValidationProblemDetails(new Dictionary<string, string[]> { ["period"] = ["Provide valid UTC dates from and to, inclusive, spanning at most 366 days."] })
            : new ProblemDetails { Title = "Dashboard temporarily unavailable." };
        problem.Status = status;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        logger.LogWarning("Dashboard returned {StatusCode}. ErrorType: {ErrorType}. TraceId: {TraceId}", status, exception.GetType().Name, context.TraceIdentifier);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: ct);
        return true;
    }
}
