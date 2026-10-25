using Clinisys.Domain;
using Clinisys.Domain.Entities;

namespace Clinisys.Tests.Domain;

public sealed class RequestResultEditabilityTests
{
    [Theory]
    [InlineData(RequestStatus.Registered, true)]
    [InlineData(RequestStatus.InProgress, true)]
    [InlineData(RequestStatus.ResultsEntered, true)]
    [InlineData(RequestStatus.Validated, false)]
    [InlineData(RequestStatus.Delivered, false)]
    [InlineData((RequestStatus)99, false)]
    public void Editability_UsesAnExplicitStatusAllowlistWithoutTransition(RequestStatus status, bool allowed)
    {
        var request = NewRequest();
        Set(request, nameof(request.Status), status);
        var before = Snapshot(request);
        if (allowed) request.EnsureResultsEditable();
        else Assert.Throws<InvalidOperationException>(request.EnsureResultsEditable);
        Assert.Equal(before, Snapshot(request));
    }

    [Theory]
    [InlineData(RequestStatus.Registered)]
    [InlineData(RequestStatus.InProgress)]
    [InlineData(RequestStatus.ResultsEntered)]
    public void EveryValidationOrDeliveryMetadata_BlocksOtherwiseEditableRequest(RequestStatus status)
    {
        foreach (var field in new[] { nameof(LaboratoryRequest.ValidatedAt), nameof(LaboratoryRequest.ValidatedBy), nameof(LaboratoryRequest.DeliveredAt) })
        {
            var request = NewRequest();
            Set(request, nameof(request.Status), status);
            // Private setters are used only to represent persisted/future workflow states.
            Set(request, field, field == nameof(request.ValidatedBy) ? "synthetic-validator" : request.RegisteredAt.AddMinutes(1));
            var before = Snapshot(request);
            Assert.Throws<InvalidOperationException>(request.EnsureResultsEditable);
            Assert.Equal(before, Snapshot(request));
        }
    }

    private static LaboratoryRequest NewRequest() => new("SYN-REQUEST",
        new Patient("Synthetic", "Test", new(2000, 1, 1), Sex.Unspecified, phoneNumber: "TEST-ONLY"), "Synthetic physician");
    private static object[] Snapshot(LaboratoryRequest request) =>
        [request.Id, request.Number, request.PatientId, request.PatientDateOfBirthSnapshot, request.PatientSexSnapshot,
            request.PrescribingPhysician, request.DiscountPercent, request.Status, request.RegisteredAt,
            request.ValidatedAt!, request.ValidatedBy!, request.DeliveredAt!, Convert.ToBase64String(request.Version)];
    private static void Set(object entity, string name, object value) => entity.GetType().GetProperty(name)!.SetValue(entity, value);
}
