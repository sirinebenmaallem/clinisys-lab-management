using System.Text.Json.Serialization;
using Clinisys.Domain;

namespace Clinisys.Application.Features.LaboratoryRequests;

public enum RequestWorkflowAction { Start, CompleteEntry, Validate, Deliver }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestWorkflowInput(string? Version);

public sealed record RequestWorkflowDto(Guid Id, RequestStatus Status, string Version,
    DateTimeOffset? ValidatedAt, string? ValidatedBy, DateTimeOffset? DeliveredAt);

public interface IRequestWorkflowStore
{
    Task<RequestWorkflowDto> TransitionAsync(Guid requestId, RequestWorkflowAction action, byte[] version, CancellationToken ct);
}

public sealed class RequestWorkflowCommands(IRequestWorkflowStore store)
{
    public Task<RequestWorkflowDto> ExecuteAsync(Guid requestId, RequestWorkflowAction action, RequestWorkflowInput input, CancellationToken ct)
    {
        if (requestId == Guid.Empty || !Enum.IsDefined(action)) throw LaboratoryRequestException.Validation("request");
        byte[] version;
        try { version = Convert.FromBase64String(input?.Version ?? ""); }
        catch (FormatException) { throw LaboratoryRequestException.Validation("version"); }
        if (version.Length != 8) throw LaboratoryRequestException.Validation("version");
        return store.TransitionAsync(requestId, action, version, ct);
    }
}
