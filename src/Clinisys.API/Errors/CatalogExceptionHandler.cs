using System.Data.Common;
using Clinisys.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.API.Errors;

public sealed class CatalogExceptionHandler(ILogger<CatalogExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested) return false;
        var status = exception switch
        {
            CatalogException { Error: CatalogError.Validation } => 400,
            CatalogException { Error: CatalogError.NotFound } => 404,
            CatalogException { Error: CatalogError.Conflict } => 409,
            SqlException { Number: 1205 } => 409,
            DbException or DbUpdateException => 503,
            _ => 500
        };
        ProblemDetails problem = exception is CatalogException { Error: CatalogError.Validation } validation
            ? new ValidationProblemDetails(validation.Errors) { Title = "One or more validation errors occurred." }
            : new ProblemDetails
            {
                Title = status switch { 404 => "Resource not found.", 409 => "Catalogue conflict.", 503 => "Database temporarily unavailable.", _ => "An unexpected error occurred." },
                Detail = exception is CatalogException known ? known.Message : null
            };
        problem.Status = status;
        problem.Type = $"https://httpstatuses.com/{status}";
        problem.Instance = context.Request.Path;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        // Do not log request bodies, search terms, connection strings or SQL error text.
        logger.LogWarning("Catalogue API returned {StatusCode}. ExceptionType: {ExceptionType}. TraceId: {TraceId}",
            status, exception.GetType().Name, context.TraceIdentifier);
        context.Response.StatusCode = status;
        if (problem is ValidationProblemDetails validationProblem)
            await context.Response.WriteAsJsonAsync(validationProblem, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        else
            await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
