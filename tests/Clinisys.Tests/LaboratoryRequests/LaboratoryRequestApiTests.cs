using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Tests.Catalog;
using Clinisys.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Tests.LaboratoryRequests;

public sealed class LaboratoryRequestApiTests
{
    private const string Route = "/api/laboratory-requests";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    [SqlServerFact]
    public async Task CreateReadAndPage_ExpandPanelsDeduplicateAndProtectHistory()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var patient = Patient();
        var first = new MedicalAnalysis(Code(), "Synthetic first", "unit", 0.01m);
        var second = new MedicalAnalysis(Code(), "Synthetic second", "unit", 0.01m);
        var panel = new AnalysisPanel(Code(), "Synthetic panel");
        var other = new AnalysisPanel(Code(), "Synthetic overlapping panel");
        session.Database.AddRange(patient, first, second, panel, other,
            new PanelAnalysis(panel.Id, first.Id), new PanelAnalysis(panel.Id, second.Id), new PanelAnalysis(other.Id, first.Id));
        await session.Database.SaveChangesAsync();
        var response = await session.Client.PostAsJsonAsync(Route,
            new LaboratoryRequestInput(patient.Id, "Synthetic physician", 50, [first.Id, first.Id], [panel.Id, other.Id]), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var created = (await response.Content.ReadFromJsonAsync<LaboratoryRequestDetailDto>(Json))!;
        Assert.Matches("^LAB-[0-9]{4}-[0-9]{6}$", created.Number);
        Assert.Equal(created.RegisteredAt.Year.ToString(), created.Number.Substring(4,4));
        Assert.Equal(TimeSpan.Zero, created.RegisteredAt.Offset);
        Assert.Equal(RequestStatus.Registered, created.Status);
        Assert.Equal(2, created.Items.Count);
        Assert.Equal(2, created.Panels.Count);
        Assert.Equal(0.02m, created.Total);
        Assert.Equal(8, Convert.FromBase64String(created.Version).Length);
        Assert.False(await session.Database.AnalysisResults.AnyAsync(x => created.Items.Select(i => i.Id).Contains(x.RequestItemId)));
        Assert.False(await session.Database.AuditEntries.AnyAsync(x => x.LaboratoryRequestId == created.Id));
        var detailResponse = await session.Client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.True(detailResponse.Headers.CacheControl?.NoStore);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<LaboratoryRequestDetailDto>(Json))!;
        Assert.Equal(created.Number, detail.Number);
        var listResponse = await session.Client.GetAsync($"{Route}?patientId={patient.Id}&number={created.Number}&status=Registered&pageSize=1");
        Assert.True(listResponse.Headers.CacheControl?.NoStore);
        var page = (await listResponse.Content.ReadFromJsonAsync<LaboratoryRequestPage>(Json))!;
        Assert.Equal(created.Id, Assert.Single(page.Items).Id);
        var raw = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("nationalIdentityNumber", raw);
        Assert.DoesNotContain("phoneNumber", raw);
        Assert.DoesNotContain("SYN-PRIVATE", raw);
        Assert.Empty((await session.Client.GetFromJsonAsync<LaboratoryRequestPage>($"{Route}?patientId={patient.Id}&page=2&pageSize=1", Json))!.Items);
        Assert.Empty((await session.Client.GetFromJsonAsync<LaboratoryRequestPage>($"{Route}?patientId={patient.Id}&registeredTo={Uri.EscapeDataString(created.RegisteredAt.ToString("O"))}", Json))!.Items);
        await session.Database.Database.ExecuteSqlInterpolatedAsync($"UPDATE MedicalAnalyses SET Price=99, Name=N'Changed' WHERE Id={first.Id}");
        await session.Database.Database.ExecuteSqlInterpolatedAsync($"UPDATE Patients SET FirstName=N'Corrected', Sex=2 WHERE Id={patient.Id}");
        await session.Database.Database.ExecuteSqlInterpolatedAsync($"DELETE PanelAnalyses WHERE AnalysisPanelId={panel.Id}");
        detail = (await session.Client.GetFromJsonAsync<LaboratoryRequestDetailDto>($"{Route}/{created.Id}", Json))!;
        Assert.Equal(0.02m, detail.Total);
        Assert.Equal(Sex.Female, detail.PatientSexSnapshot);
        Assert.Equal("Corrected", detail.Patient.CurrentFirstName);
        Assert.Equal(2, detail.Items.Count);
        Assert.Contains(detail.Items, x => x.AnalysisNameSnapshot == "Synthetic first");
    }

    [SqlServerFact]
    public async Task InvalidSelectionsAndBinding_ReturnSafeProblemsWithoutPartialRequests()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var patient = Patient();
        var active = new MedicalAnalysis(Code(), "Synthetic", "unit", 1);
        var inactive = new MedicalAnalysis(Code(), "Synthetic inactive", "unit", 1);
        inactive.Deactivate();
        var empty = new AnalysisPanel(Code(), "Synthetic empty");
        var badPanel = new AnalysisPanel(Code(), "Synthetic inactive member");
        var inactivePanel = new AnalysisPanel(Code(), "Synthetic inactive panel");
        inactivePanel.Deactivate();
        session.Database.AddRange(patient, active, inactive, empty, badPanel, inactivePanel, new PanelAnalysis(badPanel.Id, inactive.Id));
        await session.Database.SaveChangesAsync();
        var cases = new (LaboratoryRequestInput Input, HttpStatusCode Status)[]
        {
            (new(patient.Id,"Synthetic"), HttpStatusCode.BadRequest),
            (new(Guid.Empty,"Synthetic",AnalysisIds:[active.Id]), HttpStatusCode.BadRequest),
            (new(Guid.NewGuid(),"Synthetic",AnalysisIds:[active.Id]), HttpStatusCode.NotFound),
            (new(patient.Id,"Synthetic",AnalysisIds:[Guid.NewGuid()]), HttpStatusCode.NotFound),
            (new(patient.Id,"Synthetic",PanelIds:[Guid.NewGuid()]), HttpStatusCode.NotFound),
            (new(patient.Id,"Synthetic",AnalysisIds:[inactive.Id]), HttpStatusCode.Conflict),
            (new(patient.Id,"Synthetic",PanelIds:[empty.Id]), HttpStatusCode.Conflict),
            (new(patient.Id,"Synthetic",PanelIds:[badPanel.Id]), HttpStatusCode.Conflict),
            (new(patient.Id,"Synthetic",PanelIds:[inactivePanel.Id]), HttpStatusCode.Conflict),
            (new(patient.Id,"Synthetic",1.001m,[active.Id]), HttpStatusCode.BadRequest)
        };
        foreach (var test in cases) await Problem(await session.Client.PostAsJsonAsync(Route,test.Input,Json), test.Status);
        await Problem(await session.Client.PostAsync(Route, new StringContent(
            "{\"number\":\"SYN-PRIVATE\"}",Encoding.UTF8,"application/json")), HttpStatusCode.BadRequest);
        await Problem(await session.Client.GetAsync(Route + "?page=SYN-PRIVATE"), HttpStatusCode.BadRequest);
        await Problem(await session.Client.GetAsync(Route + "/" + Guid.NewGuid()), HttpStatusCode.NotFound);
        Assert.False(await session.Database.LaboratoryRequests.AnyAsync(x => x.PatientId == patient.Id));
    }

    [SqlServerFact]
    public async Task OpenApi_DescribesStrictCreationAndActualResponses()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var document = await session.Client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var path = document.GetProperty("paths").GetProperty(Route);
        var responses = path.GetProperty("post").GetProperty("responses");
        foreach (var status in new[] { "201","400","404","409","503","500" }) Assert.True(responses.TryGetProperty(status,out _));
        Assert.False(responses.TryGetProperty("200",out _));
        Assert.True(responses.GetProperty("400").GetProperty("content").TryGetProperty("application/problem+json",out _));
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var input = schemas.GetProperty(nameof(LaboratoryRequestInput));
        Assert.False(input.GetProperty("properties").TryGetProperty("number",out _));
        Assert.Equal(JsonValueKind.False,input.GetProperty("additionalProperties").ValueKind);
    }

    private static string Code() => $"SYN{Guid.NewGuid():N}"[..25];
    private static Patient Patient() => new("Synthetic","Request API",new(2000,1,1),Sex.Female,"SYN-PRIVATE-"+Guid.NewGuid().ToString("N"),"SYN-PRIVATE");
    private static async Task Problem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status,response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/problem+json",response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SYN-PRIVATE",text);
        Assert.DoesNotContain("SqlException",text);
        Assert.Equal((int)status,JsonDocument.Parse(text).RootElement.GetProperty("status").GetInt32());
    }
}
