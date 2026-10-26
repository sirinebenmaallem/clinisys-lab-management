using Clinisys.Application.Features.Authentication;
using Clinisys.Application.Features.LaboratoryReports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinisys.API.Controllers;

[ApiController]
[Route("api/laboratory-requests/{id:guid}/report")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LaboratoryReportsController(LaboratoryReportQueries queries) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthenticationPermissions.RequestsRead)]
    [ProducesResponseType(typeof(FileContentResult), 200, "application/pdf")]
    [ProducesResponseType<ValidationProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(503)]
    [ProducesResponseType<ProblemDetails>(500)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var file = await queries.GetAsync(id, ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Content, "application/pdf", file.FileName);
    }
}
