using Clinisys.Domain.Common;

namespace Clinisys.Domain.Entities;

public sealed class AuditEntry : Entity
{
    private AuditEntry() { }
    public AuditEntry(Guid laboratoryRequestId, Guid? requestItemId, AuditAction action,
        string actor, string fieldName, string? oldValue, string? newValue)
        : this(laboratoryRequestId, requestItemId, action, actor, fieldName, oldValue, newValue, DateTimeOffset.UtcNow) { }

    public AuditEntry(Guid laboratoryRequestId, Guid? requestItemId, AuditAction action,
        string actor, string fieldName, string? oldValue, string? newValue, DateTimeOffset occurredAt)
    {
        if (occurredAt.Offset != TimeSpan.Zero) throw new ArgumentException("Audit time must be UTC.", nameof(occurredAt));
        OccurredAt = occurredAt;
        LaboratoryRequestId = Guard.Id(laboratoryRequestId, nameof(laboratoryRequestId));
        RequestItemId = requestItemId is null ? null : Guard.Id(requestItemId.Value, nameof(requestItemId));
        Action = Guard.EnumValue(action, nameof(action));
        Actor = Guard.Required(actor, 100, nameof(actor));
        FieldName = Guard.Required(fieldName, 100, nameof(fieldName));
        OldValue = Guard.Optional(oldValue, 4000, nameof(oldValue));
        NewValue = Guard.Optional(newValue, 4000, nameof(newValue));
        if (OldValue is null && NewValue is null)
            throw new ArgumentException("An audit entry requires an old or new value.");
    }

    public Guid LaboratoryRequestId { get; private set; }
    public Guid? RequestItemId { get; private set; }
    public AuditAction Action { get; private set; }
    public string Actor { get; private set; } = null!;
    public string FieldName { get; private set; } = null!;
    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; } = DateTimeOffset.UtcNow;
}
