using Clinisys.API.AnalysisResults;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Application.Features.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinisys.API.Controllers;

[ApiController]
[Route("api/patients/{patientId:guid}/analyses/{analysisId:guid}/history")]
[Authorize(Policy = AuthenticationPermissions.ResultsRead)]
[ServiceFilter<ResultErrorScopeFilter>]
public sealed class PatientAnalysisHistoryController(PatientAnalysisHistoryQueries queries) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PatientAnalysisHistoryDto>(200)]
    [ProducesResponseType<ValidationProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(503)]
    [ProducesResponseType<ProblemDetails>(500)]
    public Task<PatientAnalysisHistoryDto> Get(Guid patientId, Guid analysisId, CancellationToken ct) =>
        queries.GetAsync(patientId, analysisId, ct);
}
