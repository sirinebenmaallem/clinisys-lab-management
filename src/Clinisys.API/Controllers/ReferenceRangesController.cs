using Microsoft.AspNetCore.Authorization;
using Clinisys.Application.Features.Authentication;
using Clinisys.Application.Features.ReferenceRanges;
using Microsoft.AspNetCore.Mvc;

namespace Clinisys.API.Controllers;

[ApiController]
[Route("api/medical-analyses/{analysisId:guid}/reference-ranges")]
public sealed class ReferenceRangesController(ReferenceRangeCommands commands, ReferenceRangeQueries queries) : ControllerBase
{
    [Authorize(Policy = AuthenticationPermissions.CatalogRead)]
    [HttpGet]
    public Task<IReadOnlyList<ReferenceRangeDto>> List(Guid analysisId, CancellationToken ct) => queries.ListAsync(analysisId, ct);

    [Authorize(Policy = AuthenticationPermissions.CatalogRead)]
    [HttpGet("{id:guid}")]
    public Task<ReferenceRangeDto> Get(Guid analysisId, Guid id, CancellationToken ct) => queries.GetAsync(analysisId, id, ct);

    [Authorize(Policy = AuthenticationPermissions.CatalogWrite)]
    [HttpPost]
    [ProducesResponseType<ReferenceRangeDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ReferenceRangeDto>> Create(Guid analysisId, ReferenceRangeInput input, CancellationToken ct)
    {
        var result = await commands.CreateAsync(analysisId, input, ct);
        return CreatedAtAction(nameof(Get), new { analysisId, id = result.Id }, result);
    }

    [Authorize(Policy = AuthenticationPermissions.CatalogWrite)]
    [HttpPut("{id:guid}")]
    public Task<ReferenceRangeDto> Update(Guid analysisId, Guid id, ReferenceRangeInput input, CancellationToken ct) =>
        commands.UpdateAsync(analysisId, id, input, ct);

    [Authorize(Policy = AuthenticationPermissions.CatalogWrite)]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid analysisId, Guid id, CancellationToken ct)
    {
        await commands.DeleteAsync(analysisId, id, ct);
        return NoContent();
    }
}
