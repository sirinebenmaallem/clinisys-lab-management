using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Domain;
using Clinisys.Domain.Entities;

namespace Clinisys.Tests.LaboratoryRequests;

public sealed class LaboratoryRequestUnitTests
{
    private static readonly Guid PatientId = Guid.NewGuid();
    private static readonly Guid AnalysisId = Guid.NewGuid();
    private static readonly LaboratoryRequestInput Input = new(PatientId, " Synthetic physician ", AnalysisIds: [AnalysisId]);
    private static Patient Patient() => new("Synthetic", "Test", new(2000, 1, 1), Sex.Female, phoneNumber: "TEST-ONLY");

    [Theory]
    [InlineData(2026, 1, "LAB-2026-000001")]
    [InlineData(2027, 1, "LAB-2027-000001")]
    [InlineData(2026, 999999, "LAB-2026-999999")]
    [InlineData(1, 1, "LAB-0001-000001")]
    public void NumberFormat_IsInvariant(int year, int value, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA"); Assert.Equal(expected, LaboratoryRequest.FormatNumber(year, value)); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10000, 1)]
    [InlineData(2026, 0)]
    [InlineData(2026, 1000000)]
    public void NumberFormat_RejectsInvalidCapacity(int year, int value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LaboratoryRequest.FormatNumber(year, value));

    [Fact]
    public void Register_UsesOneUtcInstantAndCapturesHistoricalFields()
    {
        var instant = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var patient = Patient();
        var request = LaboratoryRequest.Register("LAB-2026-000001", patient, " Synthetic physician ", 12.5m, instant);
        Assert.Equal(instant, request.RegisteredAt);
        Assert.Equal(RequestStatus.Registered, request.Status);
        Assert.Equal("Synthetic physician", request.PrescribingPhysician);
        Assert.Null(request.ValidatedAt);
        Assert.Null(request.ValidatedBy);
        Assert.Null(request.DeliveredAt);
        patient.Update("Corrected", "Synthetic", new(2001, 1, 1), Sex.Male, phoneNumber: "TEST-ONLY");
        Assert.Equal(new DateOnly(2000, 1, 1), request.PatientDateOfBirthSnapshot);
        Assert.Equal(Sex.Female, request.PatientSexSnapshot);
    }

    [Theory]
    [InlineData("TEST")]
    [InlineData("LAB-2027-000001")]
    [InlineData("LAB-2026-000000")]
    [InlineData("LAB-2026-1000000")]
    [InlineData("lab-2026-000001")]
    public void Register_RejectsInvalidOrMismatchedNumber(string number) =>
        Assert.ThrowsAny<ArgumentException>(() => LaboratoryRequest.Register(number, Patient(), "Synthetic", 0,
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void Register_RejectsNonUtcAndBirthAfterRegistration()
    {
        Assert.Throws<ArgumentException>(() => LaboratoryRequest.Register("LAB-2026-000001", Patient(), "Synthetic", 0,
            new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(1))));
        Assert.Throws<ArgumentException>(() => LaboratoryRequest.Register("LAB-1999-000001", Patient(), "Synthetic", 0,
            new(1999, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData(0, 0.01)]
    [InlineData(50, 0.01)]
    [InlineData(100, 0)]
    public void Money_UsesSumOfRoundedSnapshots(decimal discount, decimal expectedLine)
    {
        var request = LaboratoryRequest.Register("LAB-2026-000001", Patient(), "Synthetic", discount,
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var analysis = new MedicalAnalysis("SYN", "Synthetic", "unit", 0.01m);
        var first = new RequestItem(request, analysis);
        var second = new RequestItem(request, new MedicalAnalysis("SYN2", "Synthetic", "unit", 0.01m));
        analysis.Update("CHANGED", "Changed", "different", 100);
        Assert.Equal(expectedLine, first.NetPrice);
        Assert.Equal(2 * expectedLine, new[] { first, second }.Sum(x => x.NetPrice));
        Assert.Equal("SYN", first.AnalysisCodeSnapshot);
        Assert.Equal("unit", first.UnitSnapshot);
        Assert.Equal(0.01m, first.UnitPriceSnapshot);
    }

    [Fact]
    public async Task Commands_DeduplicateAndNormalizeBeforePersistence()
    {
        var store = new FakeStore();
        var panel = Guid.NewGuid();
        await new LaboratoryRequestCommands(store).CreateAsync(Input with
        { AnalysisIds = [AnalysisId, AnalysisId], PanelIds = [panel, panel] }, default);
        Assert.Equal("Synthetic physician", store.Input!.PrescribingPhysician);
        Assert.Single(store.Input.AnalysisIds!);
        Assert.Single(store.Input.PanelIds!);
    }

    [Fact]
    public async Task Commands_RejectAllInvalidInputsWithoutCallingStore()
    {
        foreach (var invalid in new[]
        {
            Input with { PatientId = Guid.Empty }, Input with { PrescribingPhysician = " " },
            Input with { PrescribingPhysician = new string('x',201) }, Input with { DiscountPercent = -1 },
            Input with { DiscountPercent = 101 }, Input with { DiscountPercent = 1.001m },
            Input with { AnalysisIds = [], PanelIds = [] }, Input with { AnalysisIds = [Guid.Empty] },
            Input with { PanelIds = [Guid.Empty] }, Input with { AnalysisIds = Enumerable.Repeat(AnalysisId,501).ToArray() }
        })
        {
            var store = new FakeStore();
            Assert.Equal(LaboratoryRequestError.Validation,
                (await Assert.ThrowsAsync<LaboratoryRequestException>(() => new LaboratoryRequestCommands(store).CreateAsync(invalid, default))).Error);
            Assert.Null(store.Input);
        }
    }

    [Theory]
    [InlineData("number")]
    [InlineData("registeredAt")]
    [InlineData("status")]
    [InlineData("unitPrice")]
    [InlineData("total")]
    [InlineData("netPrice")]
    [InlineData("validatedBy")]
    [InlineData("actor")]
    [InlineData("patientVersion")]
    public void JsonContract_RejectsForbiddenFields(string field)
    {
        var json = "{\"patientId\":\"" + PatientId + "\",\"prescribingPhysician\":\"Synthetic\",\"" + field + "\":\"SYN-SECRET\"}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LaboratoryRequestInput>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void JsonContract_DefaultsDiscountAndDoesNotChangeOtherContracts()
    {
        var input = JsonSerializer.Deserialize<LaboratoryRequestInput>("{\"patientId\":\"" + PatientId + "\",\"prescribingPhysician\":\"Synthetic\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(0, input.DiscountPercent);
        Assert.Null(input.AnalysisIds);
        var other = JsonSerializer.Deserialize<Clinisys.Application.Features.Patients.PatientInput>(
            "{\"firstName\":\"Synthetic\",\"lastName\":\"Test\",\"extra\":1}", new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(other);
    }

    [Fact]
    public async Task Queries_ValidateBeforeCallingStoreAndMapMissingResource()
    {
        var store = new FakeStore();
        var queries = new LaboratoryRequestQueries(store);
        foreach (var query in new[]
        {
            new LaboratoryRequestPageQuery { Page = 0 }, new LaboratoryRequestPageQuery { PageSize = 101 },
            new LaboratoryRequestPageQuery { Page = int.MaxValue, PageSize = 100 },
            new LaboratoryRequestPageQuery { PatientId = Guid.Empty },
            new LaboratoryRequestPageQuery { Status = (RequestStatus)99 },
            new LaboratoryRequestPageQuery { Number = new string('x',31) },
            new LaboratoryRequestPageQuery { RegisteredFrom = DateTimeOffset.MaxValue, RegisteredTo = DateTimeOffset.MinValue }
        }) await Assert.ThrowsAsync<LaboratoryRequestException>(() => queries.SearchAsync(query, default));
        Assert.Equal(0, store.SearchCount);
        await queries.SearchAsync(new(), default);
        Assert.Equal(1, store.SearchCount);
        Assert.Equal(LaboratoryRequestError.NotFound,
            (await Assert.ThrowsAsync<LaboratoryRequestException>(() => queries.GetAsync(Guid.NewGuid(), default))).Error);
    }

    private sealed class FakeStore : ILaboratoryRequestStore
    {
        public LaboratoryRequestInput? Input { get; private set; }
        public int SearchCount { get; private set; }
        public Task<LaboratoryRequestDetailDto> CreateAsync(LaboratoryRequestInput input, CancellationToken ct)
        { Input = input; return Task.FromResult<LaboratoryRequestDetailDto>(null!); }
        public Task<LaboratoryRequestDetailDto?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<LaboratoryRequestDetailDto?>(null);
        public Task<LaboratoryRequestPage> SearchAsync(LaboratoryRequestPageQuery query, CancellationToken ct)
        { SearchCount++; return Task.FromResult(new LaboratoryRequestPage([],0,query.Page,query.PageSize)); }
    }
}
