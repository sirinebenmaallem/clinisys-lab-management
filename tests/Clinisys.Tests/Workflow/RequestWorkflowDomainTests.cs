using Clinisys.Domain;
using Clinisys.Domain.Entities;

namespace Clinisys.Tests.Workflow;

public sealed class RequestWorkflowDomainTests
{
    private static readonly DateTimeOffset Registered = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Registered.AddHours(1);
    private static LaboratoryRequest Request() => LaboratoryRequest.Register("LAB-2026-000001",
        new Patient("Synthetic", "Workflow", new(2000, 1, 1), Sex.Unspecified, phoneNumber: "TEST-ONLY"),
        "Synthetic", 0, Registered);

    [Fact]
    public void FullWorkflow_RecordsValidationDeliveryAndLocksResults()
    {
        var request = Request();
        request.StartProcessing();
        Assert.Equal(RequestStatus.InProgress, request.Status);
        request.MarkResultsEntered(true);
        Assert.Equal(RequestStatus.ResultsEntered, request.Status);
        request.ValidateResults(true, "synthetic-biologist", Now);
        Assert.Equal(RequestStatus.Validated, request.Status);
        Assert.Equal(Now, request.ValidatedAt);
        Assert.Equal("synthetic-biologist", request.ValidatedBy);
        Assert.Throws<InvalidOperationException>(request.EnsureResultsEditable);
        request.Deliver(Now.AddMinutes(1));
        Assert.Equal(RequestStatus.Delivered, request.Status);
        Assert.Equal(Now.AddMinutes(1), request.DeliveredAt);
        Assert.Throws<InvalidOperationException>(request.EnsureResultsEditable);
    }

    [Theory]
    [InlineData(RequestStatus.Registered)]
    [InlineData(RequestStatus.InProgress)]
    [InlineData(RequestStatus.ResultsEntered)]
    [InlineData(RequestStatus.Validated)]
    [InlineData(RequestStatus.Delivered)]
    public void OnlyTheNextTransitionIsAllowed_AndFailuresAreAtomic(RequestStatus status)
    {
        foreach (var action in new[] { "start", "complete", "validate", "deliver" })
        {
            var request = Request();
            if (status >= RequestStatus.InProgress) request.StartProcessing();
            if (status >= RequestStatus.ResultsEntered) request.MarkResultsEntered(true);
            if (status >= RequestStatus.Validated) request.ValidateResults(true, "biologist", Now);
            if (status >= RequestStatus.Delivered) request.Deliver(Now);
            var before = (request.Status, request.ValidatedAt, request.ValidatedBy, request.DeliveredAt);
            var expected = action switch
            {
                "start" => RequestStatus.Registered, "complete" => RequestStatus.InProgress,
                "validate" => RequestStatus.ResultsEntered, _ => RequestStatus.Validated
            };
            Action transition = () =>
            {
                switch (action)
                {
                    case "start": request.StartProcessing(); break;
                    case "complete": request.MarkResultsEntered(true); break;
                    case "validate": request.ValidateResults(true, "biologist", Now); break;
                    default: request.Deliver(Now); break;
                }
            };
            if (status == expected) transition();
            else
            {
                Assert.Throws<InvalidOperationException>(transition);
                Assert.Equal(before, (request.Status, request.ValidatedAt, request.ValidatedBy, request.DeliveredAt));
            }
        }
    }

    [Fact]
    public void MissingResults_RefuseCompletionAndValidationWithoutMutation()
    {
        var request = Request();
        request.StartProcessing();
        Assert.Throws<InvalidOperationException>(() => request.MarkResultsEntered(false));
        Assert.Equal(RequestStatus.InProgress, request.Status);
        request.MarkResultsEntered(true);
        Assert.Throws<InvalidOperationException>(() => request.ValidateResults(false, "biologist", Now));
        Assert.Equal(RequestStatus.ResultsEntered, request.Status);
        Assert.Null(request.ValidatedAt);
        Assert.Null(request.ValidatedBy);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("date")]
    [InlineData("offset")]
    public void InvalidValidationMetadata_DoesNotMutate(string reason)
    {
        var request = Request();
        request.StartProcessing();
        request.MarkResultsEntered(true);
        var instant = reason == "date" ? Registered.AddSeconds(-1) : reason == "offset" ? Now.ToOffset(TimeSpan.FromHours(1)) : Now;
        Assert.ThrowsAny<ArgumentException>(() => request.ValidateResults(true, reason == "actor" ? " " : "biologist", instant));
        Assert.Equal(RequestStatus.ResultsEntered, request.Status);
        Assert.Null(request.ValidatedAt);
    }

    [Fact]
    public void ResultValidation_LocksValueAndPreservesFrozenReferencesAndEntry()
    {
        var result = new AnalysisResult(Guid.NewGuid(), 0, "technician", new(-1, 1), Registered);
        result.Validate("biologist", Now);
        Assert.Throws<InvalidOperationException>(() => result.UpdateValue(2, "other", Now));
        Assert.Throws<InvalidOperationException>(() => result.Validate("other", Now));
        Assert.Equal(0, result.Value);
        Assert.Equal(-1, result.ReferenceLowerBoundSnapshot);
        Assert.Equal(1, result.ReferenceUpperBoundSnapshot);
        Assert.Equal("technician", result.EnteredBy);
        Assert.Equal(Registered, result.EnteredAt);
        Assert.Equal(Now, result.ValidatedAt);
        Assert.Equal("biologist", result.ValidatedBy);
    }

    [Fact]
    public void InvalidResultValidationAndDeliveryTime_DoNotMutate()
    {
        var result = new AnalysisResult(Guid.NewGuid(), 0, "technician", null, Now);
        Assert.Throws<ArgumentException>(() => result.Validate("biologist", Registered));
        Assert.Throws<ArgumentException>(() => result.Validate(" ", Now));
        Assert.Null(result.ValidatedAt);
        Assert.Null(result.ValidatedBy);
        var request = Request();
        request.StartProcessing(); request.MarkResultsEntered(true); request.ValidateResults(true, "biologist", Now);
        Assert.Throws<ArgumentException>(() => request.Deliver(Registered));
        Assert.Equal(RequestStatus.Validated, request.Status);
        Assert.Null(request.DeliveredAt);
    }
}
