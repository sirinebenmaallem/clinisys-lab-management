using Clinisys.Application.Features.Patients;
using Clinisys.Domain;
using Clinisys.Domain.Entities;

namespace Clinisys.Tests.Patients;

public sealed class PatientApplicationTests
{
    private static readonly PatientInput Input = new("Synthetic", "Test", new(2000, 1, 1), Sex.Unspecified, "SYN-ID");

    [Fact]
    public async Task CommandsPassDecodedClientVersionAndNormalizedDraftToStore()
    {
        var store = new RecordingStore();
        var version = new byte[] { 0, 0, 0, 0, 0, 0, 0, 7 };
        var id = Guid.NewGuid();
        await new PatientCommands(store).UpdateAsync(id,
            new(Input with { NationalIdentityNumber = " syn-id " }, Convert.ToBase64String(version)), default);
        Assert.Equal(id, store.Id);
        Assert.Equal(version, store.Version);
        Assert.Equal("SYN-ID", store.Draft!.NationalIdentityNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AAAA")]
    public async Task InvalidVersionNeverReachesPersistence(string? version)
    {
        var store = new RecordingStore();
        var error = await Assert.ThrowsAsync<PatientException>(() =>
            new PatientCommands(store).UpdateAsync(Guid.NewGuid(), new(Input, version!), default));
        Assert.Equal(PatientError.Validation, error.Error);
        Assert.Null(store.Draft);
    }

    [Fact]
    public async Task InvalidDomainInputAndMissingPatientsUseSafeErrors()
    {
        var store = new RecordingStore();
        var error = await Assert.ThrowsAsync<PatientException>(() =>
            new PatientCommands(store).CreateAsync(Input with { NationalIdentityNumber = "SYN-SECRET", PhoneNumber = " " }, default));
        Assert.DoesNotContain("SYN-SECRET", error.Message);
        Assert.Null(store.Draft);
        var missing = await Assert.ThrowsAsync<PatientException>(() => new PatientQueries(store).GetAsync(Guid.NewGuid(), default));
        Assert.Equal(PatientError.NotFound, missing.Error);
    }

    private sealed class RecordingStore : IPatientStore
    {
        public Guid Id { get; private set; }
        public Patient? Draft { get; private set; }
        public byte[]? Version { get; private set; }
        public Task<PatientDetailDto?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<PatientDetailDto?>(null);
        public Task<PatientPage> SearchAsync(PatientPageQuery query, CancellationToken ct) => throw new NotSupportedException();
        public Task<PatientDetailDto> CreateAsync(Patient patient, CancellationToken ct) => throw new NotSupportedException();
        public Task<PatientDetailDto> UpdateAsync(Guid id, Patient draft, byte[] version, CancellationToken ct)
        {
            (Id, Draft, Version) = (id, draft, version);
            return Task.FromResult(new PatientDetailDto(id, draft.FirstName, draft.LastName, draft.DateOfBirth,
                draft.Sex, draft.NationalIdentityNumber, draft.PhoneNumber, Convert.ToBase64String(version)));
        }
    }
}
