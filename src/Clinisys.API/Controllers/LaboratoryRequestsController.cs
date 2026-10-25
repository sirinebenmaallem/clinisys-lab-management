using Clinisys.Application.Features.LaboratoryRequests;
using Microsoft.AspNetCore.Mvc;

namespace Clinisys.API.Controllers;

[ApiController]
[Route("api/laboratory-requests")]
public sealed class LaboratoryRequestsController(LaboratoryRequestCommands commands, LaboratoryRequestQueries queries) : ControllerBase
{
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

    [HttpGet]
    [ProducesResponseType<LaboratoryRequestPage>(200)]
    [ProducesResponseType<ValidationProblemDetails>(400, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(409, "application/problem+json", Description = "Exceptional SQL deadlock; not a business read conflict.")]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(500, "application/problem+json")]
    public Task<LaboratoryRequestPage> List([FromQuery] LaboratoryRequestPageQuery query, CancellationToken ct) => queries.SearchAsync(query, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<LaboratoryRequestDetailDto>(200)]
    [ProducesResponseType<ProblemDetails>(404, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(409, "application/problem+json", Description = "Exceptional SQL deadlock; not a business read conflict.")]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(500, "application/problem+json")]
    public Task<LaboratoryRequestDetailDto> Get(Guid id, CancellationToken ct) => queries.GetAsync(id, ct);
}
