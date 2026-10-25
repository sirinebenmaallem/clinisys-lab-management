using System.Data.Common;
using Clinisys.API.AnalysisResults;
using Clinisys.Application.Features.AnalysisResults;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.API.Errors;

public sealed class AnalysisResultExceptionHandler(ILogger<AnalysisResultExceptionHandler> logger) : IExceptionHandler
{
    public static readonly object RequestMarker = new();
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var known = exception as AnalysisResultException;
        if (known is null && !context.Items.ContainsKey(RequestMarker)) return false;
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested) return false;
        var technical = exception;
        while ((technical is InvalidOperationException or DbUpdateException) && technical.InnerException is { } inner)
            technical = inner;
        var status = known is not null ? known.Error switch
        {
            AnalysisResultError.Validation => 400, AnalysisResultError.NotFound => 404,
            AnalysisResultError.Conflict => 409, AnalysisResultError.CommitUnknown => 503, _ => 500
        } : technical switch
        {
            SqlException { Number: 1205 } => 409,
            DbException => 503,
            _ => exception is DbUpdateException ? 503 : 500
        };
        var unknown = known?.Error == AnalysisResultError.CommitUnknown;
        // Do not echo arbitrary exception messages, submitted values or SQL text.
        ProblemDetails problem = status == 400
            ? new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [known?.Field is "value" or "version" or "identifiers" ? known.Field : "result"] = ["Result input is missing or invalid."]
            }) { Title = "One or more validation errors occurred." }
            : new ProblemDetails
            {
                Title = status switch
                {
                    404 => "Resource not found.", 409 => "Result conflict.",
                    503 => "Result operation temporarily unavailable.", _ => "An unexpected error occurred."
                },
                Detail = unknown ? "The transaction outcome could not be confirmed. Verify the result before submitting again." : null
            };
        problem.Status = status;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (unknown) problem.Extensions["outcomeUnknown"] = true;
        logger.LogWarning("Result API returned {StatusCode}. ErrorType: {ErrorType}. TraceId: {TraceId}",
            status, exception.GetType().Name, context.TraceIdentifier);
        context.Response.StatusCode = status;
        if (problem is ValidationProblemDetails validation)
            await context.Response.WriteAsJsonAsync(validation, options: null, contentType: "application/problem+json", cancellationToken: ct);
        else
            await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: ct);
        return true;
    }
}
