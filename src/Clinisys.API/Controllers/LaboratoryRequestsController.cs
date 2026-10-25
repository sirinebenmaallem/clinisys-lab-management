using Microsoft.AspNetCore.Authorization;
using Clinisys.Application.Features.Authentication;
using Clinisys.Application.Features.LaboratoryRequests;
using Microsoft.AspNetCore.Mvc;

namespace Clinisys.API.Controllers;

[ApiController]
[Route("api/laboratory-requests")]
public sealed class LaboratoryRequestsController(LaboratoryRequestCommands commands, LaboratoryRequestQueries queries) : ControllerBase
{
    [Authorize(Policy = AuthenticationPermissions.RequestsCreate)]
    [HttpPost]
    [ProducesResponseType<LaboratoryRequestDetailDto>(201)]
    [ProducesResponseType<ValidationProblemDetails>(400, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(404, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(409, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(500, "application/problem+json")]
    public async Task<ActionResult<LaboratoryRequestDetailDto>> Create(LaboratoryRequestInput input, CancellationToken ct)
    {
        var result = await commands.CreateAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [Authorize(Policy = AuthenticationPermissions.RequestsRead)]
    [HttpGet]
    [ProducesResponseType<LaboratoryRequestPage>(200)]
    [ProducesResponseType<ValidationProblemDetails>(400, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(409, "application/problem+json", Description = "Exceptional SQL deadlock; not a business read conflict.")]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(500, "application/problem+json")]
    public Task<LaboratoryRequestPage> List([FromQuery] LaboratoryRequestPageQuery query, CancellationToken ct) => queries.SearchAsync(query, ct);

    [Authorize(Policy = AuthenticationPermissions.RequestsRead)]
    [HttpGet("{id:guid}")]
    [ProducesResponseType<LaboratoryRequestDetailDto>(200)]
    [ProducesResponseType<ProblemDetails>(404, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(409, "application/problem+json", Description = "Exceptional SQL deadlock; not a business read conflict.")]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(500, "application/problem+json")]
    public Task<LaboratoryRequestDetailDto> Get(Guid id, CancellationToken ct) => queries.GetAsync(id, ct);
}
