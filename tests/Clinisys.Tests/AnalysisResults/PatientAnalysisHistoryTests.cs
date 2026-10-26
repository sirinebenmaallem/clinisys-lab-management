using Clinisys.Application.Features.AnalysisResults;

namespace Clinisys.Tests.AnalysisResults;

public sealed class PatientAnalysisHistoryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmptyIdentifiers_RejectBeforeStore(bool emptyPatient)
    {
        var store = new Store();
        var error = await Assert.ThrowsAsync<AnalysisResultException>(() => new PatientAnalysisHistoryQueries(store)
            .GetAsync(emptyPatient ? Guid.Empty : Guid.NewGuid(), emptyPatient ? Guid.NewGuid() : Guid.Empty, default));
        Assert.Equal(AnalysisResultError.Validation, error.Error);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task MissingPair_Is404_ExistingPairWithoutResultsIsEmpty()
    {
        var patient = Guid.NewGuid(); var analysis = Guid.NewGuid();
        var store = new Store(); var queries = new PatientAnalysisHistoryQueries(store);
        var error = await Assert.ThrowsAsync<AnalysisResultException>(() => queries.GetAsync(patient, analysis, default));
        Assert.Equal(AnalysisResultError.NotFound, error.Error);
        store.Data = new(patient, analysis, []);
        Assert.Empty((await queries.GetAsync(patient, analysis, default)).Points);
        Assert.Equal((patient, analysis), store.Ids);
    }

    private sealed class Store : IPatientAnalysisHistoryStore
    {
        public int Calls;
        public (Guid, Guid) Ids;
        public PatientAnalysisHistoryDto? Data;
        public Task<PatientAnalysisHistoryDto?> GetAsync(Guid patientId, Guid analysisId, CancellationToken ct)
        { Calls++; Ids = (patientId, analysisId); return Task.FromResult(Data); }
    }
}
