using System.Text.Json;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Domain;
using Clinisys.Domain.Entities;

namespace Clinisys.Tests.AnalysisResults;

// No context, host or database: the fake only exercises Application port contracts.
public sealed class AnalysisResultApplicationTests
{
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();
    private static readonly byte[] Version = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("0")]
    [InlineData("-0.123456")]
    [InlineData("999999999999.999999")]
    [InlineData("-999999999999.999999")]
    public async Task Commands_ForwardValidValuesIdsVersionAndCancellation(string text)
    {
        var store = new FakeStore { Detail = Detail() };
        var commands = new AnalysisResultCommands(store);
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        using var cancellation = new CancellationTokenSource();
        Assert.Same(store.Detail, await commands.CreateAsync(RequestId, ItemId, new(value), cancellation.Token));
        Assert.Equal(1, store.Creates);
        Assert.Equal(0, store.Updates);
        Assert.Same(store.Detail, await commands.UpdateAsync(RequestId, ItemId, new(value, Convert.ToBase64String(Version)), cancellation.Token));
        Assert.Equal(1, store.Updates);
        Assert.Equal((RequestId, ItemId, value, cancellation.Token), store.LastWrite);
        Assert.Equal(Version, store.ClientVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0.0000001")]
    [InlineData("1000000000000")]
    [InlineData("-1000000000000")]
    public async Task Commands_RejectInvalidValuesBeforeCallingStore(string? text)
    {
        decimal? value = text is null ? null : decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var store = new FakeStore();
        var commands = new AnalysisResultCommands(store);
        await Validation(() => commands.CreateAsync(RequestId, ItemId, new(value), default), "value");
        await Validation(() => commands.UpdateAsync(RequestId, ItemId, new(value, Convert.ToBase64String(Version)), default), "value");
        Assert.Equal(0, store.Creates + store.Updates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-base64")]
    [InlineData("AQ==")]
    [InlineData("AQIDBAUGBw==")]
    [InlineData("AQIDBAUGBwgJ")]
    public async Task Update_RejectsMissingMalformedAndWrongLengthVersion(string? version)
    {
        var store = new FakeStore();
        await Validation(() => new AnalysisResultCommands(store).UpdateAsync(RequestId, ItemId, new(0, version), default), "version");
        Assert.Equal(0, store.Updates);
    }

    [Fact]
    public async Task Commands_RejectNullInputsAndEmptyIdsWithoutWrites()
    {
        var store = new FakeStore();
        var commands = new AnalysisResultCommands(store);
        await Validation(() => commands.CreateAsync(RequestId, ItemId, null!, default), "value");
        await Validation(() => commands.UpdateAsync(RequestId, ItemId, null!, default), "value");
        foreach (var ids in new[] { (Guid.Empty, ItemId), (RequestId, Guid.Empty) })
        {
            await Validation(() => commands.CreateAsync(ids.Item1, ids.Item2, new(0), default), "identifiers");
            await Validation(() => commands.UpdateAsync(ids.Item1, ids.Item2, new(0, Convert.ToBase64String(Version)), default), "identifiers");
        }
        Assert.Equal(0, store.Creates + store.Updates);
    }

    [Fact]
    public async Task Update_MissingResultDoesNotFallBackToCreate()
    {
        var store = new FakeStore { WriteFailure = AnalysisResultException.NotFound() };
        var error = await Assert.ThrowsAsync<AnalysisResultException>(() => new AnalysisResultCommands(store)
            .UpdateAsync(RequestId, ItemId, new(1, Convert.ToBase64String(Version)), default));
        Assert.Equal(AnalysisResultError.NotFound, error.Error);
        Assert.Equal(1, store.Updates);
        Assert.Equal(0, store.Creates);
    }

    [Theory]
    [InlineData(AnalysisResultError.Conflict)]
    [InlineData(AnalysisResultError.Internal)]
    [InlineData(AnalysisResultError.CommitUnknown)]
    public async Task Writes_PropagateStoreErrorsWithoutRetryOrUpsert(AnalysisResultError error)
    {
        var failure = error switch
        {
            AnalysisResultError.Conflict => AnalysisResultException.Conflict(),
            AnalysisResultError.Internal => AnalysisResultException.Internal(),
            _ => AnalysisResultException.Unknown()
        };
        var store = new FakeStore { WriteFailure = failure };
        var commands = new AnalysisResultCommands(store);
        Assert.Same(failure, await Assert.ThrowsAsync<AnalysisResultException>(() => commands.CreateAsync(RequestId, ItemId, new(1), default)));
        Assert.Same(failure, await Assert.ThrowsAsync<AnalysisResultException>(() => commands.UpdateAsync(RequestId, ItemId, new(1, Convert.ToBase64String(Version)), default)));
        Assert.Equal(1, store.Creates);
        Assert.Equal(1, store.Updates);
    }

    [Fact]
    public async Task Queries_DistinguishMissingRequestAndResultFromUnenteredItems()
    {
        var store = new FakeStore();
        var queries = new AnalysisResultQueries(store);
        Assert.Equal(AnalysisResultError.NotFound, (await Assert.ThrowsAsync<AnalysisResultException>(() => queries.GetAsync(RequestId, ItemId, default))).Error);
        Assert.Equal(AnalysisResultError.NotFound, (await Assert.ThrowsAsync<AnalysisResultException>(() => queries.ListForRequestAsync(RequestId, default))).Error);
        store.Detail = Detail();
        store.List = new(RequestId, [new(ItemId, store.Detail.MedicalAnalysisId, "SYN", "Synthetic", "unit", null)]);
        using var cancellation = new CancellationTokenSource();
        Assert.Same(store.Detail, await queries.GetAsync(RequestId, ItemId, cancellation.Token));
        var list = await queries.ListForRequestAsync(RequestId, cancellation.Token);
        Assert.Same(store.List, list);
        Assert.Null(Assert.Single(list.Items).Result);
        Assert.Equal((RequestId, ItemId, cancellation.Token), store.LastRead);
        Assert.Equal(cancellation.Token, store.ListToken);
        Assert.Equal(0, store.Creates + store.Updates);
    }

    [Fact]
    public async Task Queries_RejectEmptyIdsBeforeCallingStore()
    {
        var store = new FakeStore();
        var queries = new AnalysisResultQueries(store);
        await Validation(() => queries.GetAsync(Guid.Empty, ItemId, default), "identifiers");
        await Validation(() => queries.GetAsync(RequestId, Guid.Empty, default), "identifiers");
        await Validation(() => queries.ListForRequestAsync(Guid.Empty, default), "identifiers");
        Assert.Equal(0, store.Reads);
    }

    [Theory]
    [InlineData("enteredBy")]
    [InlineData("enteredAt")]
    [InlineData("validatedBy")]
    [InlineData("validatedAt")]
    [InlineData("referenceLowerBoundSnapshot")]
    [InlineData("referenceUpperBoundSnapshot")]
    [InlineData("abnormality")]
    [InlineData("status")]
    [InlineData("price")]
    [InlineData("requestItemId")]
    public void InputContracts_RejectClientControlledMetadata(string field)
    {
        var json = "{\"value\":0,\"" + field + "\":\"SYN-PRIVATE\"}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AnalysisResultInput>(json, Json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AnalysisResultUpdateInput>(json, Json));
    }

    [Fact]
    public async Task InputContracts_DistinguishMissingNullAndZero()
    {
        var store = new FakeStore();
        var commands = new AnalysisResultCommands(store);
        foreach (var json in new[] { "{}", "{\"value\":null}" })
            await Validation(() => commands.CreateAsync(RequestId, ItemId, JsonSerializer.Deserialize<AnalysisResultInput>(json, Json)!, default), "value");
        await commands.CreateAsync(RequestId, ItemId, JsonSerializer.Deserialize<AnalysisResultInput>("{\"value\":0}", Json)!, default);
        Assert.Equal(1, store.Creates);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AnalysisResultInput>("{\"value\":\"secret\"}", Json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AnalysisResultInput>("{\"value\":0,\"version\":\"secret\"}", Json));
    }

    [Fact]
    public void OutputContractsAndErrors_DoNotRequirePatientIdentityOrPhone()
    {
        var json = JsonSerializer.Serialize(new AnalysisResultListDto(RequestId,
            [new(ItemId, Guid.NewGuid(), "SYN", "Synthetic", "unit", Detail())]), Json);
        foreach (var field in new[] { "nationalIdentityNumber", "phoneNumber", "dateOfBirth", "patientId" })
            Assert.DoesNotContain(field, json);
        foreach (var type in new[] { typeof(AnalysisResultInput), typeof(AnalysisResultUpdateInput) })
            Assert.All(type.GetProperties(), x => Assert.Contains(x.Name, new[] { "Value", "Version" }));
        Assert.Equal(AnalysisResultError.Conflict, AnalysisResultException.AmbiguousReference().Error);
        Assert.Equal(AnalysisResultError.Conflict, AnalysisResultException.IncompatibleUnit().Error);
        Assert.Equal(AnalysisResultError.CommitUnknown, AnalysisResultException.Unknown().Error);
        Assert.DoesNotContain("SYN-PRIVATE", AnalysisResultException.Internal().Message);
    }

    [Fact]
    public async Task CorrectionContract_PassesOnlyValueAndVersion_PreservesExistingBoundsInDomain()
    {
        var result = new AnalysisResult(ItemId, 1, "synthetic", new(-2, 2), DateTimeOffset.UnixEpoch);
        var store = new FakeStore { ResultToCorrect = result };
        await new AnalysisResultCommands(store).UpdateAsync(RequestId, ItemId, new(3, Convert.ToBase64String(Version)), default);
        Assert.Equal(-2, result.ReferenceLowerBoundSnapshot);
        Assert.Equal(2, result.ReferenceUpperBoundSnapshot);
        Assert.Equal(Abnormality.High, result.Abnormality);
        Assert.Equal(0, store.Creates);
        // This verifies the Application contract plus Domain behavior, not SQL concurrency.
    }

    private static async Task Validation(Func<Task> action, string field)
    {
        var error = await Assert.ThrowsAsync<AnalysisResultException>(action);
        Assert.Equal(AnalysisResultError.Validation, error.Error);
        Assert.Equal(field, error.Field);
    }

    private static AnalysisResultDetailDto Detail() => new(Guid.NewGuid(), RequestId, ItemId, Guid.NewGuid(),
        "SYN", "Synthetic", "unit", 1, -2, 2, Abnormality.Normal, DateTimeOffset.UnixEpoch, "synthetic", null, null,
        Convert.ToBase64String(Version));

    private sealed class FakeStore : IAnalysisResultStore
    {
        public int Creates, Updates, Reads;
        public AnalysisResultDetailDto? Detail;
        public AnalysisResultListDto? List;
        public AnalysisResultException? WriteFailure;
        public AnalysisResult? ResultToCorrect;
        public byte[]? ClientVersion;
        public (Guid, Guid, decimal, CancellationToken) LastWrite;
        public (Guid, Guid, CancellationToken) LastRead;
        public CancellationToken ListToken;
        public Task<AnalysisResultDetailDto> CreateAsync(Guid requestId, Guid itemId, decimal value, CancellationToken ct)
        {
            Creates++;
            LastWrite = (requestId, itemId, value, ct);
            if (WriteFailure is not null) throw WriteFailure;
            return Task.FromResult(Detail!);
        }
        public Task<AnalysisResultDetailDto> UpdateAsync(Guid requestId, Guid itemId, decimal value, byte[] version, CancellationToken ct)
        {
            Updates++;
            LastWrite = (requestId, itemId, value, ct);
            ClientVersion = version;
            if (WriteFailure is not null) throw WriteFailure;
            ResultToCorrect?.UpdateValue(value, "synthetic-corrector", DateTimeOffset.UnixEpoch.AddMinutes(1));
            return Task.FromResult(Detail!);
        }
        public Task<AnalysisResultDetailDto?> GetAsync(Guid requestId, Guid itemId, CancellationToken ct)
        {
            Reads++;
            LastRead = (requestId, itemId, ct);
            return Task.FromResult(Detail);
        }
        public Task<AnalysisResultListDto?> ListForRequestAsync(Guid requestId, CancellationToken ct)
        {
            Reads++;
            ListToken = ct;
            Assert.Equal(RequestId, requestId);
            return Task.FromResult(List);
        }
    }
}
