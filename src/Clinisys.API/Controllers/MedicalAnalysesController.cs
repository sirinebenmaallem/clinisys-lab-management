using Clinisys.Application.Common;
using Clinisys.Application.Features.MedicalAnalyses;
using Microsoft.AspNetCore.Mvc;

namespace Clinisys.API.Controllers;

[ApiController]
[Route("api/medical-analyses")]
public sealed class MedicalAnalysesController(MedicalAnalysisCommands commands, MedicalAnalysisQueries queries) : ControllerBase
{
    [HttpGet]
    public Task<CatalogPage<AnalysisDto>> List([FromQuery] CatalogPageQuery query, CancellationToken ct) => queries.SearchAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<AnalysisDto> Get(Guid id, CancellationToken ct) => queries.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<AnalysisDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AnalysisDto>> Create(AnalysisInput input, CancellationToken ct)
    {
        var result = await commands.CreateAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public Task<AnalysisDto> Update(Guid id, AnalysisInput input, CancellationToken ct) => commands.UpdateAsync(id, input, ct);

    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await commands.DeactivateAsync(id, ct);
        return NoContent();
    }
}
