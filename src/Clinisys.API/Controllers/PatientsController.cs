using Microsoft.AspNetCore.Authorization;
using Clinisys.Application.Features.Authentication;
using Clinisys.Application.Features.Patients;
using Microsoft.AspNetCore.Mvc;

namespace Clinisys.API.Controllers;

[ApiController]
[Route("api/patients")]
public sealed class PatientsController(PatientCommands commands, PatientQueries queries) : ControllerBase
{
    [Authorize(Policy = AuthenticationPermissions.PatientsSearch)]
    [HttpGet]
    [ProducesResponseType<PatientPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json",
        Description = "Exceptional SQL deadlock handled by the global exception handler; not a business read conflict.")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public Task<PatientPage> List([FromQuery] PatientPageQuery query, CancellationToken ct) => queries.SearchAsync(query, ct);
    [Authorize(Policy = AuthenticationPermissions.PatientsReadDetail)]
    [HttpGet("{id:guid}")]
    [ProducesResponseType<PatientDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json",
        Description = "Exceptional SQL deadlock handled by the global exception handler; not a business read conflict.")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public Task<PatientDetailDto> Get(Guid id, CancellationToken ct) => queries.GetAsync(id, ct);
    [Authorize(Policy = AuthenticationPermissions.PatientsWrite)]
    [HttpPost]
    [ProducesResponseType<PatientDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<PatientDetailDto>> Create(PatientInput input, CancellationToken ct)
    {
        var result = await commands.CreateAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [Authorize(Policy = AuthenticationPermissions.PatientsWrite)]
    [HttpPut("{id:guid}")]
    [ProducesResponseType<PatientDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public Task<PatientDetailDto> Update(Guid id, PatientUpdateInput input, CancellationToken ct) => commands.UpdateAsync(id, input, ct);
}
