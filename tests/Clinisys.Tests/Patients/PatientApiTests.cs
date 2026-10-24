using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clinisys.Application.Features.Patients;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Tests.Catalog;
using Clinisys.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Tests.Patients;

public sealed class PatientApiTests
{
    private const string Route = "/api/patients";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    private static string Token() => "SYN-" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
    private static PatientInput Input(string? identity = null, string? phone = "SYN-PHONE") =>
        new("Synthetic", "Test", new(2000, 1, 1), Sex.Unspecified, identity, phone);
    private static async Task<PatientDetailDto> Create(HttpClient client, PatientInput input)
    {
        var response = await client.PostAsJsonAsync(Route, input, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<PatientDetailDto>(Json))!;
    }
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode status, params string[] secrets)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal((int)status, JsonDocument.Parse(text).RootElement.GetProperty("status").GetInt32());
        foreach (var secret in secrets) Assert.DoesNotContain(secret, text);
        Assert.DoesNotContain("SqlException", text);
    }

    [SqlServerFact]
    public async Task Lifecycle_UsesClientVersionAndRejectsStaleUpdatesWithoutDataLoss()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var input = Input(Token());
        var created = await Create(session.Client, input);
        Assert.Equal(8, Convert.FromBase64String(created.Version).Length);
        var route = $"{Route}/{created.Id}";
        var read = await session.Client.GetAsync(route);
        Assert.True(read.Headers.CacheControl?.NoStore);
        Assert.Equal(created, await read.Content.ReadFromJsonAsync<PatientDetailDto>(Json));
        var changed = input with { FirstName = "Changed", NationalIdentityNumber = null };
        var update = await session.Client.PutAsJsonAsync(route, new PatientUpdateInput(changed, created.Version), Json);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var current = (await update.Content.ReadFromJsonAsync<PatientDetailDto>(Json))!;
        Assert.NotEqual(created.Version, current.Version);
        Assert.Null(current.NationalIdentityNumber);
        await Problem(await session.Client.PutAsJsonAsync(route, new PatientUpdateInput(input, created.Version), Json), HttpStatusCode.Conflict, input.NationalIdentityNumber!);
        Assert.Equal(current, await session.Client.GetFromJsonAsync<PatientDetailDto>(route, Json));
        var clearPhone = changed with { NationalIdentityNumber = Token(), PhoneNumber = null };
        var nextResponse = await session.Client.PutAsJsonAsync(route, new PatientUpdateInput(clearPhone, current.Version), Json);
        Assert.Equal(HttpStatusCode.OK, nextResponse.StatusCode);
        var next = (await nextResponse.Content.ReadFromJsonAsync<PatientDetailDto>(Json))!;
        Assert.Null(next.PhoneNumber);
        // Even an unchanged PUT must check its token and return a fresh version.
        var unchanged = await session.Client.PutAsJsonAsync(route, new PatientUpdateInput(clearPhone, next.Version), Json);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.NotEqual(next.Version, (await unchanged.Content.ReadFromJsonAsync<PatientDetailDto>(Json))!.Version);
        await Problem(await session.Client.GetAsync($"{Route}/{Guid.NewGuid()}"), HttpStatusCode.NotFound);
        await Problem(await session.Client.PutAsJsonAsync($"{Route}/{Guid.NewGuid()}", new PatientUpdateInput(input, next.Version), Json), HttpStatusCode.NotFound);
    }

    [SqlServerFact]
    public async Task IdentityUniqueness_AllowsSharedPhonesAndRejectsDuplicateCreatesAndUpdates()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var id = Token();
        var first = await Create(session.Client, Input(" " + id.ToLowerInvariant() + " "));
        Assert.Equal(id, first.NationalIdentityNumber);
        await Problem(await session.Client.PostAsJsonAsync(Route, Input(id), Json), HttpStatusCode.Conflict, id, "SYN-PHONE");
        var second = await Create(session.Client, Input());
        await Create(session.Client, Input());
        await Problem(await session.Client.PutAsJsonAsync($"{Route}/{second.Id}", new PatientUpdateInput(Input(id), second.Version), Json), HttpStatusCode.Conflict, id);
        Assert.Null((await session.Client.GetFromJsonAsync<PatientDetailDto>($"{Route}/{second.Id}", Json))!.NationalIdentityNumber);
    }

    [SqlServerFact]
    public async Task Validation_RejectsInvalidInputAndVersionsWithoutEchoingValues()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var input = Input(Token());
        var created = await Create(session.Client, input);
        foreach (var invalid in new[]
        {
            input with { FirstName = " " }, input with { LastName = null! },
            input with { FirstName = new string('x', 101) }, input with { LastName = new string('x', 101) },
            input with { DateOfBirth = null }, input with { DateOfBirth = default(DateOnly) },
            input with { DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1) },
            input with { Sex = null }, input with { Sex = (Sex)99 },
            input with { NationalIdentityNumber = null, PhoneNumber = null },
            input with { PhoneNumber = " " }, input with { NationalIdentityNumber = new string('x', 51) },
            input with { PhoneNumber = new string('x', 31) }
        })
        {
            await Problem(await session.Client.PostAsJsonAsync(Route, invalid, Json), HttpStatusCode.BadRequest, input.NationalIdentityNumber!);
            await Problem(await session.Client.PutAsJsonAsync($"{Route}/{created.Id}", new PatientUpdateInput(invalid, created.Version), Json), HttpStatusCode.BadRequest);
        }
        foreach (var version in new[] { "", "SYN-SECRET-NOT-BASE64", "AAAA", Convert.ToBase64String(new byte[9]) })
            await Problem(await session.Client.PutAsJsonAsync($"{Route}/{created.Id}", new PatientUpdateInput(input, version), Json), HttpStatusCode.BadRequest, "SYN-SECRET-NOT-BASE64");
        await Problem(await session.Client.PutAsJsonAsync($"{Route}/{created.Id}", new { patient = input }, Json), HttpStatusCode.BadRequest);
        foreach (var body in new[] { "{\"firstName\":\"Synthetic\",\"sex\":123}", "{\"dateOfBirth\":\"SYN-SECRET-DATE\"}", "{\"sex\":\"SYN-SECRET-SEX\"}" })
            await Problem(await session.Client.PostAsync(Route, new StringContent(body, Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest, "SYN-SECRET-DATE", "SYN-SECRET-SEX");
        await Problem(await session.Client.GetAsync(Route + "?page=SYN-SECRET-QUERY"), HttpStatusCode.BadRequest, "SYN-SECRET-QUERY");
        Assert.Equal(created, await session.Client.GetFromJsonAsync<PatientDetailDto>($"{Route}/{created.Id}", Json));
        foreach (var sex in new[] { Sex.Unspecified, Sex.Female, Sex.Male })
            await Create(session.Client, Input() with { Sex = sex, DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow) });
    }

    [SqlServerFact]
    public async Task SearchAndPaging_HaveStableOrderExactIdentifiersAndPrivateLists()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var marker = Token();
        var identity = Token();
        var phone = Token();
        var a = await Create(session.Client, Input(identity, phone) with { FirstName = "Alpha", LastName = marker });
        var b = await Create(session.Client, Input() with { FirstName = "Beta", LastName = marker });
        var c = await Create(session.Client, Input() with { FirstName = marker, LastName = "Other" });
        async Task<PatientPage> Search(string term, string extra = "") =>
            (await session.Client.GetFromJsonAsync<PatientPage>($"{Route}?search={Uri.EscapeDataString(term)}{extra}", Json))!;
        var page = await Search(" " + marker + " ", "&pageSize=1");
        Assert.Equal(3, page.TotalCount);
        var all = await Search(marker);
        Assert.Equal(1, all.Page);
        Assert.Equal(20, all.PageSize);
        Assert.Equal(all.Items[0], Assert.Single(page.Items));
        Assert.Equal(all.Items[1], Assert.Single((await Search(marker, "&page=2&pageSize=1")).Items));
        Assert.Equal(new[] { a.Id, b.Id }, all.Items.Where(x => x.LastName == marker).Select(x => x.Id));
        Assert.Equal(a.Id, Assert.Single((await Search(identity.ToLowerInvariant())).Items).Id);
        Assert.Equal(a.Id, Assert.Single((await Search(" " + phone + " ")).Items).Id);
        Assert.Empty((await Search(identity[..^1])).Items);
        Assert.Empty((await Search(phone[..^1])).Items);
        Assert.Empty((await Search(marker, "&page=100")).Items);
        var listResponse = await session.Client.GetAsync($"{Route}?search={marker}");
        Assert.True(listResponse.Headers.CacheControl?.NoStore);
        var raw = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("nationalIdentityNumber", raw);
        Assert.DoesNotContain("phoneNumber", raw);
        Assert.DoesNotContain(identity, raw);
        Assert.DoesNotContain(phone, raw);
        foreach (var query in new[] { "page=0", "pageSize=101", "pageSize=0", "page=2147483647&pageSize=100", "search=" + new string('x', 201) })
            await Problem(await session.Client.GetAsync(Route + "?" + query), HttpStatusCode.BadRequest);
        var literal = Token() + "%_[]";
        var special = await Create(session.Client, Input() with { LastName = literal });
        Assert.Equal(special.Id, Assert.Single((await Search(literal)).Items).Id);
    }

    [SqlServerFact]
    public async Task PatientUpdates_LeaveHistoricalRequestsResultsAndAuditUnchanged()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var patient = new Patient("Synthetic", "History", new(2000, 1, 1), Sex.Male, phoneNumber: "TEST-ONLY");
        var request = new LaboratoryRequest(Token()[..25], patient, "Synthetic physician");
        var analysis = new MedicalAnalysis(Token()[..25], "Synthetic", "unit", 1);
        var item = new RequestItem(request, analysis);
        var result = new AnalysisResult(item.Id, 1, "synthetic-actor");
        var audit = new AuditEntry(request.Id, item.Id, AuditAction.Created, "synthetic-actor", "Value", null, "1");
        session.Database.AddRange(patient, request, analysis, item, result, audit);
        await session.Database.SaveChangesAsync();
        var requestVersion = request.Version.ToArray();
        var resultVersion = result.Version.ToArray();
        var auditVersion = audit.Version.ToArray();
        var response = await session.Client.PutAsJsonAsync($"{Route}/{patient.Id}",
            new PatientUpdateInput(Input() with { DateOfBirth = new(2001, 2, 3), Sex = Sex.Female }, Convert.ToBase64String(patient.Version)), Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        session.Database.ChangeTracker.Clear();
        var historical = await session.Database.LaboratoryRequests.SingleAsync(x => x.Id == request.Id);
        Assert.Equal(new DateOnly(2000, 1, 1), historical.PatientDateOfBirthSnapshot);
        Assert.Equal(Sex.Male, historical.PatientSexSnapshot);
        Assert.Equal(requestVersion, historical.Version);
        Assert.Equal(resultVersion, (await session.Database.AnalysisResults.SingleAsync(x => x.Id == result.Id)).Version);
        Assert.Equal(auditVersion, (await session.Database.AuditEntries.SingleAsync(x => x.Id == audit.Id)).Version);
    }
}
