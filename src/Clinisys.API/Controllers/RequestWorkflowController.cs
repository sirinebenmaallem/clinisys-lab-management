using Clinisys.Application.Features.Authentication;
using Clinisys.Application.Features.LaboratoryRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Clinisys.API.Controllers;

[ApiController]
[Route("api/laboratory-requests/{id:guid}")]
[ProducesResponseType<RequestWorkflowDto>(200)]
[ProducesResponseType<ValidationProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(503)]
[ProducesResponseType<ProblemDetails>(500)]
public sealed class RequestWorkflowController(RequestWorkflowCommands commands) : ControllerBase
{
    [HttpPost("start")]
    [Authorize(Policy = AuthenticationPermissions.RequestsProcess)]
    public Task<RequestWorkflowDto> Start(Guid id, RequestWorkflowInput input, CancellationToken ct) =>
        commands.ExecuteAsync(id, RequestWorkflowAction.Start, input, ct);

    [HttpPost("complete-entry")]
    [Authorize(Policy = AuthenticationPermissions.RequestsProcess)]
    public Task<RequestWorkflowDto> CompleteEntry(Guid id, RequestWorkflowInput input, CancellationToken ct) =>
        commands.ExecuteAsync(id, RequestWorkflowAction.CompleteEntry, input, ct);

    [HttpPost("validate")]
    [Authorize(Policy = AuthenticationPermissions.RequestsValidate)]
    public Task<RequestWorkflowDto> ValidateResults(Guid id, RequestWorkflowInput input, CancellationToken ct) =>
        commands.ExecuteAsync(id, RequestWorkflowAction.Validate, input, ct);

    [HttpPost("deliver")]
    [Authorize(Policy = AuthenticationPermissions.RequestsDeliver)]
    public Task<RequestWorkflowDto> Deliver(Guid id, RequestWorkflowInput input, CancellationToken ct) =>
        commands.ExecuteAsync(id, RequestWorkflowAction.Deliver, input, ct);
}
