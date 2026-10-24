using Clinisys.Application.Features.Patients;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Clinisys.API.Errors;

public sealed class PatientExceptionHandler(ILogger<PatientExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not PatientException patient) return false;
        var status = patient.Error switch { PatientError.Validation => 400, PatientError.NotFound => 404, _ => 409 };
        ProblemDetails problem = patient.Error == PatientError.Validation
            ? new ValidationProblemDetails(new Dictionary<string, string[]> { [patient.Field ?? "patient"] = [patient.Message] })
                { Title = "One or more validation errors occurred." }
            : new ProblemDetails { Title = status == 404 ? "Resource not found." : "Patient conflict.", Detail = patient.Message };
        problem.Status = status;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        logger.LogWarning("Patient API returned {StatusCode}. TraceId: {TraceId}", status, context.TraceIdentifier);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: ct);
        return true;
    }
}
