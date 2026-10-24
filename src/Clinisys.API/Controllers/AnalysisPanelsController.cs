using Clinisys.Application.Common;
using Clinisys.Application.Features.AnalysisPanels;
using Microsoft.AspNetCore.Mvc;

namespace Clinisys.API.Controllers;

[ApiController]
[Route("api/analysis-panels")]
public sealed class AnalysisPanelsController(AnalysisPanelCommands commands, AnalysisPanelQueries queries) : ControllerBase
{
    [HttpGet]
    public Task<CatalogPage<PanelDto>> List([FromQuery] CatalogPageQuery query, CancellationToken ct) => queries.SearchAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<PanelDetailsDto> Get(Guid id, CancellationToken ct) => queries.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<PanelDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PanelDto>> Create(PanelInput input, CancellationToken ct)
    {
        var result = await commands.CreateAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public Task<PanelDto> Update(Guid id, PanelInput input, CancellationToken ct) => commands.UpdateAsync(id, input, ct);

    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await commands.DeactivateAsync(id, ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/analyses/{analysisId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AddAnalysis(Guid id, Guid analysisId, CancellationToken ct)
    {
        await commands.AddAnalysisAsync(id, analysisId, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/analyses/{analysisId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveAnalysis(Guid id, Guid analysisId, CancellationToken ct)
    {
        await commands.RemoveAnalysisAsync(id, analysisId, ct);
        return NoContent();
    }
}
