using Clinisys.Application.Features.Authentication;
using Clinisys.Application.Features.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinisys.API.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = AuthenticationPermissions.DashboardRead)]
public sealed class DashboardController(DashboardQueries queries) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<DashboardDto>(200)]
    [ProducesResponseType<ValidationProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(503)]
    [ProducesResponseType<ProblemDetails>(500)]
    public Task<DashboardDto> Get([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        queries.GetAsync(from, to, ct);
}
