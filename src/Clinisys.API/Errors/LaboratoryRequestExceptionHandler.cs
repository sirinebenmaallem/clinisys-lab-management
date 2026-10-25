using Clinisys.Application.Features.LaboratoryRequests;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Clinisys.API.Errors;

public sealed class LaboratoryRequestExceptionHandler(ILogger<LaboratoryRequestExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not LaboratoryRequestException known) return false;
        var status = known.Error switch
        { LaboratoryRequestError.Validation => 400, LaboratoryRequestError.NotFound => 404,
            LaboratoryRequestError.Conflict => 409, LaboratoryRequestError.CommitUnknown => 503, _ => 500 };
        ProblemDetails problem = status == 400
            ? new ValidationProblemDetails(new Dictionary<string, string[]> { [known.Field ?? "request"] = [known.Message] })
                { Title = "One or more validation errors occurred." }
            : new ProblemDetails { Title = "Laboratory request could not be completed.", Detail = known.Message };
        problem.Status = status;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (known.Error == LaboratoryRequestError.CommitUnknown) problem.Extensions["outcomeUnknown"] = true;
        logger.LogWarning("Laboratory request API returned {StatusCode}. Error: {Error}. TraceId: {TraceId}",
            status, known.Error, context.TraceIdentifier);
        context.Response.StatusCode = status;
        if (problem is ValidationProblemDetails validation)
            await context.Response.WriteAsJsonAsync(validation, options: null, contentType: "application/problem+json", cancellationToken: ct);
        else
            await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: ct);
        return true;
    }
}
