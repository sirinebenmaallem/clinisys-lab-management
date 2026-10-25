using Clinisys.API.AnalysisResults;
using Clinisys.Application.Features.AnalysisResults;
using Microsoft.AspNetCore.Mvc;

namespace Clinisys.API.Controllers;

[ApiController]
[Route("api/laboratory-requests/{requestId:guid}")]
[ServiceFilter<AnalysisResultLocalAccessFilter>]
public sealed class AnalysisResultsController(AnalysisResultCommands commands, AnalysisResultQueries queries) : ControllerBase
{
    [HttpPost("items/{itemId:guid}/result")]
    [ProducesResponseType<AnalysisResultDetailDto>(201)]
    [ProducesResponseType<ValidationProblemDetails>(400, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(404, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(409, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(500, "application/problem+json")]
    public async Task<ActionResult<AnalysisResultDetailDto>> Create(Guid requestId, Guid itemId,
        AnalysisResultInput input, CancellationToken ct)
    {
        var result = await commands.CreateAsync(requestId, itemId, input, ct);
        return CreatedAtAction(nameof(Get), new { requestId, itemId }, result);
    }

    [HttpPut("items/{itemId:guid}/result")]
    [ProducesResponseType<AnalysisResultDetailDto>(200)]
    [ProducesResponseType<ValidationProblemDetails>(400, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(404, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(409, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(500, "application/problem+json")]
    public Task<AnalysisResultDetailDto> Update(Guid requestId, Guid itemId, AnalysisResultUpdateInput input, CancellationToken ct) =>
        commands.UpdateAsync(requestId, itemId, input, ct);

    [HttpGet("items/{itemId:guid}/result")]
    [ProducesResponseType<AnalysisResultDetailDto>(200)]
    [ProducesResponseType<ValidationProblemDetails>(400, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(404, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(409, "application/problem+json", Description = "Exceptional SQL deadlock; not a business read conflict.")]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(500, "application/problem+json")]
    public Task<AnalysisResultDetailDto> Get(Guid requestId, Guid itemId, CancellationToken ct) => queries.GetAsync(requestId, itemId, ct);

    [HttpGet("results")]
    [ProducesResponseType<AnalysisResultListDto>(200)]
    [ProducesResponseType<ValidationProblemDetails>(400, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(404, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(409, "application/problem+json", Description = "Exceptional SQL deadlock; not a business read conflict.")]
    [ProducesResponseType<ProblemDetails>(503, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(500, "application/problem+json")]
    public Task<AnalysisResultListDto> List(Guid requestId, CancellationToken ct) => queries.ListForRequestAsync(requestId, ct);
}
