using Clinisys.Application.Features.ReferenceRanges;
using Microsoft.AspNetCore.Mvc;

namespace Clinisys.API.Controllers;

[ApiController]
[Route("api/medical-analyses/{analysisId:guid}/reference-ranges")]
public sealed class ReferenceRangesController(ReferenceRangeCommands commands, ReferenceRangeQueries queries) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ReferenceRangeDto>> List(Guid analysisId, CancellationToken ct) => queries.ListAsync(analysisId, ct);

    [HttpGet("{id:guid}")]
    public Task<ReferenceRangeDto> Get(Guid analysisId, Guid id, CancellationToken ct) => queries.GetAsync(analysisId, id, ct);

    [HttpPost]
    [ProducesResponseType<ReferenceRangeDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ReferenceRangeDto>> Create(Guid analysisId, ReferenceRangeInput input, CancellationToken ct)
    {
        var result = await commands.CreateAsync(analysisId, input, ct);
        return CreatedAtAction(nameof(Get), new { analysisId, id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public Task<ReferenceRangeDto> Update(Guid analysisId, Guid id, ReferenceRangeInput input, CancellationToken ct) =>
        commands.UpdateAsync(analysisId, id, input, ct);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid analysisId, Guid id, CancellationToken ct)
    {
        await commands.DeleteAsync(analysisId, id, ct);
        return NoContent();
    }
}
