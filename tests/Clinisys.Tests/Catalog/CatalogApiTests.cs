using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clinisys.Application.Common;
using Clinisys.Application.Features.AnalysisPanels;
using Clinisys.Application.Features.MedicalAnalyses;
using Clinisys.Application.Features.ReferenceRanges;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Tests.Catalog;

public sealed class CatalogApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private const string Analyses = "/api/medical-analyses";
    private const string Panels = "/api/analysis-panels";

    [SqlServerFact]
    public async Task Analyses_CreateGetUpdateAndDeactivate_ReturnCorrectHttpResponses()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var created = await CreateAnalysis(session.Client);
        var fetched = await Get<AnalysisDto>(session.Client, $"{Analyses}/{created.Id}");
        Assert.Equal(created, fetched);
        var updated = await session.Client.PutAsJsonAsync($"{Analyses}/{created.Id}", new { code = Code(), name = "Updated synthetic", unit = "unit", price = 12.34m });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(12.34m, (await Read<AnalysisDto>(updated)).Price);
        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Equal(HttpStatusCode.NoContent, (await session.Client.PostAsync($"{Analyses}/{created.Id}/deactivate", null)).StatusCode);
        Assert.False((await Get<AnalysisDto>(session.Client, $"{Analyses}/{created.Id}")).IsActive);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await session.Client.DeleteAsync($"{Analyses}/{created.Id}")).StatusCode);
    }

    [SqlServerFact]
    public async Task DuplicateAnalysisCodes_AreConflictsOnCreateAndUpdate()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var first = await CreateAnalysis(session.Client);
        var second = await CreateAnalysis(session.Client);
        var input = new { code = " " + first.Code.ToLowerInvariant() + " ", name = "Synthetic duplicate", unit = "unit", price = 1 };
        await ExpectProblem(await session.Client.PostAsJsonAsync(Analyses, input), HttpStatusCode.Conflict);
        await ExpectProblem(await session.Client.PutAsJsonAsync($"{Analyses}/{second.Id}", input), HttpStatusCode.Conflict);
        Assert.Equal(second.Code, (await Get<AnalysisDto>(session.Client, $"{Analyses}/{second.Id}")).Code);
    }

    [SqlServerFact]
    public async Task InvalidPricesMissingFieldsAndInvalidQueries_ReturnValidationProblems()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var existing = await CreateAnalysis(session.Client);
        foreach (var price in new[] { -0.01m, 0.001m, 10000000000000000m })
        {
            await ExpectProblem(await session.Client.PostAsJsonAsync(Analyses, new { code = Code(), name = "Synthetic", unit = "unit", price }), HttpStatusCode.BadRequest, true);
            await ExpectProblem(await session.Client.PutAsJsonAsync($"{Analyses}/{existing.Id}", new { code = existing.Code, name = "Synthetic", unit = "unit", price }), HttpStatusCode.BadRequest, true);
        }
        Assert.Equal(existing.Price, (await Get<AnalysisDto>(session.Client, $"{Analyses}/{existing.Id}")).Price);
        foreach (var body in new[] { "{}", "null", "{\"code\":\"TEST\",\"name\":\"Synthetic\",\"unit\":\"unit\"}", "{bad-json}", "{\"code\":\" \",\"name\":\"Synthetic\",\"unit\":\"unit\",\"price\":1}" })
            await ExpectProblem(await session.Client.PostAsync(Analyses, new StringContent(body, Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest, true);
        foreach (var query in new[] { "page=0", "pageSize=101", "page=2147483647&pageSize=100", "isActive=invalid", "search=" + new string('a', 201) })
            await ExpectProblem(await session.Client.GetAsync($"{Analyses}?{query}"), HttpStatusCode.BadRequest, true);
    }

    [SqlServerFact]
    public async Task SearchAndPagination_UseCodeOrNameStableOrderingAndActiveFilter()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var prefix = Code()[..18];
        var records = new List<AnalysisDto>();
        foreach (var suffix in new[] { "C", "A", "B" })
            records.Add(await CreateAnalysis(session.Client, prefix + suffix, "Synthetic " + prefix + suffix));
        var first = await Get<CatalogPage<AnalysisDto>>(session.Client, $"{Analyses}?search={prefix}&pageSize=2");
        var second = await Get<CatalogPage<AnalysisDto>>(session.Client, $"{Analyses}?search={prefix}&pageSize=2&page=2");
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(new[] { prefix + "A", prefix + "B" }, first.Items.Select(x => x.Code));
        Assert.Equal(prefix + "C", Assert.Single(second.Items).Code);
        Assert.Empty((await Get<CatalogPage<AnalysisDto>>(session.Client, $"{Analyses}?search={prefix}&page=99")).Items);
        var byName = await Get<CatalogPage<AnalysisDto>>(session.Client, $"{Analyses}?search={Uri.EscapeDataString("Synthetic " + prefix + "B")}");
        Assert.Single(byName.Items);
        await session.Client.PostAsync($"{Analyses}/{records[0].Id}/deactivate", null);
        Assert.Equal(2, (await Get<CatalogPage<AnalysisDto>>(session.Client, $"{Analyses}?search={prefix}&isActive=true")).TotalCount);
        Assert.Empty((await Get<CatalogPage<AnalysisDto>>(session.Client, $"{Analyses}?search={Uri.EscapeDataString("' OR 1=1 --")}")).Items);
    }

    [SqlServerFact]
    public async Task ReferenceRanges_CreateReadUpdateAndDelete_RespectParentScope()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var analysis = await CreateAnalysis(session.Client);
        var other = await CreateAnalysis(session.Client);
        var route = $"{Analyses}/{analysis.Id}/reference-ranges";
        var response = await session.Client.PostAsJsonAsync(route, Range(0, 10), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var range = await Read<ReferenceRangeDto>(response);
        Assert.Equal(range, await Get<ReferenceRangeDto>(session.Client, response.Headers.Location!.ToString()));
        var changed = await session.Client.PutAsJsonAsync($"{route}/{range.Id}", Range(0, 20, Sex.Male, -2, 3), Json);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(Sex.Male, (await Read<ReferenceRangeDto>(changed)).Sex);
        Assert.Single(await Get<List<ReferenceRangeDto>>(session.Client, route));
        await ExpectProblem(await session.Client.GetAsync($"{Analyses}/{other.Id}/reference-ranges/{range.Id}"), HttpStatusCode.NotFound);
        await ExpectProblem(await session.Client.DeleteAsync($"{Analyses}/{other.Id}/reference-ranges/{range.Id}"), HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.DeleteAsync($"{route}/{range.Id}")).StatusCode);
        Assert.Empty(await Get<List<ReferenceRangeDto>>(session.Client, route));
    }

    [SqlServerFact]
    public async Task ReferenceRanges_RejectInvalidInputsAndOverlapsOnCreateAndUpdate()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var analysis = await CreateAnalysis(session.Client);
        var route = $"{Analyses}/{analysis.Id}/reference-ranges";
        var firstRange = await session.Client.PostAsJsonAsync(route, Range(0, 10), Json);
        Assert.Equal(HttpStatusCode.Created, firstRange.StatusCode);
        var firstRangeId = (await Read<ReferenceRangeDto>(firstRange)).Id;
        foreach (var invalid in new[] { Range(-1, 10), Range(10, 10), Range(20, 10), Range(0, 10, lower: 2, upper: 1), Range(0, 10, lower: 0.0000001m) })
        {
            await ExpectProblem(await session.Client.PostAsJsonAsync(route, invalid, Json), HttpStatusCode.BadRequest, true);
            await ExpectProblem(await session.Client.PutAsJsonAsync($"{route}/{firstRangeId}", invalid, Json), HttpStatusCode.BadRequest, true);
        }
        await ExpectProblem(await session.Client.PostAsJsonAsync(route, new { sex = "Unknown", minimumAgeDays = 0, lowerBound = 0, upperBound = 1 }), HttpStatusCode.BadRequest, true);
        await ExpectProblem(await session.Client.PostAsJsonAsync(route, new { sex = 1, minimumAgeDays = 0, lowerBound = 0, upperBound = 1 }), HttpStatusCode.BadRequest, true);
        await ExpectProblem(await session.Client.PostAsJsonAsync(route, new { sex = "Female", lowerBound = 0, upperBound = 1 }), HttpStatusCode.BadRequest, true);
        foreach (var overlap in new[] { Range(0, 10), Range(5, 20), Range(0, null) })
            await ExpectProblem(await session.Client.PostAsJsonAsync(route, overlap, Json), HttpStatusCode.Conflict);
        var adjacent = await session.Client.PostAsJsonAsync(route, Range(10, null), Json);
        Assert.Equal(HttpStatusCode.Created, adjacent.StatusCode);
        var adjacentId = (await Read<ReferenceRangeDto>(adjacent)).Id;
        await ExpectProblem(await session.Client.PutAsJsonAsync($"{route}/{adjacentId}", Range(9, null), Json), HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.OK, (await session.Client.PutAsJsonAsync($"{route}/{adjacentId}", Range(10, null), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await session.Client.PostAsJsonAsync(route, Range(0, null, Sex.Male), Json)).StatusCode);
    }

    [SqlServerFact]
    public async Task UnitChangeAndInactiveAnalysis_DoNotSilentlyInvalidateReferenceRanges()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var analysis = await CreateAnalysis(session.Client);
        var route = $"{Analyses}/{analysis.Id}/reference-ranges";
        Assert.Equal(HttpStatusCode.Created, (await session.Client.PostAsJsonAsync(route, Range(0, null), Json)).StatusCode);
        await ExpectProblem(await session.Client.PutAsJsonAsync($"{Analyses}/{analysis.Id}", new { analysis.Code, analysis.Name, unit = "different-unit", analysis.Price }), HttpStatusCode.Conflict);
        await session.Client.PostAsync($"{Analyses}/{analysis.Id}/deactivate", null);
        await ExpectProblem(await session.Client.PostAsJsonAsync(route, Range(0, 10, Sex.Male), Json), HttpStatusCode.Conflict);
        Assert.Single(await Get<List<ReferenceRangeDto>>(session.Client, route));
    }

    [SqlServerFact]
    public async Task Panels_CreateUpdateSearchAssociateRemoveAndDeactivate()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var analysis = await CreateAnalysis(session.Client);
        var panel = await CreatePanel(session.Client);
        var otherPanel = await CreatePanel(session.Client);
        await ExpectProblem(await session.Client.PutAsJsonAsync($"{Panels}/{otherPanel.Id}", new { code = panel.Code, name = "Duplicate synthetic panel" }), HttpStatusCode.Conflict);
        Assert.Equal(otherPanel.Code, (await Get<PanelDetailsDto>(session.Client, $"{Panels}/{otherPanel.Id}")).Code);
        await ExpectProblem(await session.Client.PostAsJsonAsync(Panels, new { code = " ", name = "Synthetic" }), HttpStatusCode.BadRequest, true);
        var changedName = "Synthetic " + Code();
        var updated = await session.Client.PutAsJsonAsync($"{Panels}/{panel.Id}", new { code = panel.Code, name = changedName });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(changedName, (await Read<PanelDto>(updated)).Name);
        Assert.Single((await Get<CatalogPage<PanelDto>>(session.Client, $"{Panels}?search={panel.Code}&pageSize=1")).Items);
        Assert.Single((await Get<CatalogPage<PanelDto>>(session.Client, $"{Panels}?search={Uri.EscapeDataString(changedName)}")).Items);
        var association = $"{Panels}/{panel.Id}/analyses/{analysis.Id}";
        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.PutAsync(association, null)).StatusCode);
        await ExpectProblem(await session.Client.PutAsync(association, null), HttpStatusCode.Conflict);
        Assert.Equal(analysis.Id, Assert.Single((await Get<PanelDetailsDto>(session.Client, $"{Panels}/{panel.Id}")).Analyses).Id);
        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.PostAsync($"{Panels}/{panel.Id}/deactivate", null)).StatusCode);
        Assert.False((await Get<PanelDetailsDto>(session.Client, $"{Panels}/{panel.Id}")).IsActive);
        Assert.Single((await Get<CatalogPage<PanelDto>>(session.Client, $"{Panels}?search={panel.Code}&isActive=false")).Items);
        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.DeleteAsync(association)).StatusCode);
        Assert.Empty((await Get<PanelDetailsDto>(session.Client, $"{Panels}/{panel.Id}")).Analyses);
        await ExpectProblem(await session.Client.PutAsync(association, null), HttpStatusCode.Conflict);
        await ExpectProblem(await session.Client.DeleteAsync(association), HttpStatusCode.NotFound);
        await ExpectProblem(await session.Client.PostAsJsonAsync(Panels, new { panel.Code, name = "Duplicate synthetic" }), HttpStatusCode.Conflict);
    }

    [SqlServerFact]
    public async Task MissingResourcesAndInactiveAssociations_ReturnConsistentErrors()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var analysis = await CreateAnalysis(session.Client);
        var panel = await CreatePanel(session.Client);
        foreach (var route in new[] { $"{Analyses}/{Guid.NewGuid()}", $"{Panels}/{Guid.NewGuid()}", $"{Analyses}/{Guid.NewGuid()}/reference-ranges" })
            await ExpectProblem(await session.Client.GetAsync(route), HttpStatusCode.NotFound);
        await ExpectProblem(await session.Client.PutAsJsonAsync($"{Analyses}/{Guid.NewGuid()}", new { code = Code(), name = "Synthetic", unit = "unit", price = 1 }), HttpStatusCode.NotFound);
        await ExpectProblem(await session.Client.PutAsync($"{Panels}/{panel.Id}/analyses/{Guid.NewGuid()}", null), HttpStatusCode.NotFound);
        await session.Client.PostAsync($"{Analyses}/{analysis.Id}/deactivate", null);
        await ExpectProblem(await session.Client.PutAsync($"{Panels}/{panel.Id}/analyses/{analysis.Id}", null), HttpStatusCode.Conflict);
        await ExpectProblem(await session.Client.GetAsync($"{Panels}?pageSize=0"), HttpStatusCode.BadRequest, true);
    }

    [SqlServerFact]
    public async Task CatalogueChanges_DoNotAlterHistoricalRequestItemsResultsOrSelectedPanels()
    {
        await using var session = await CatalogApiSession.OpenAsync();
        var analysis = new MedicalAnalysis(Code(), "Synthetic history", "unit", 10);
        var panel = new AnalysisPanel(Code(), "Synthetic history panel");
        var patient = new Patient("Synthetic", "Test", new DateOnly(2000, 1, 1), Sex.Unspecified, phoneNumber: "TEST-ONLY");
        var request = new LaboratoryRequest(Code(), patient, "test-physician", 10);
        var item = new RequestItem(request, analysis);
        var selectedPanel = new RequestPanel(request.Id, panel);
        var range = new ReferenceRange(analysis.Id, Sex.Unspecified, 0, null, new ReferenceInterval(0, 2));
        var result = new AnalysisResult(item.Id, 1, "test-actor", new ReferenceInterval(0, 2));
        var audit = new AuditEntry(request.Id, item.Id, AuditAction.Created, "test-actor", "Value", null, "1");
        session.Database.AddRange(analysis, panel, patient, request, item, selectedPanel, range, result, audit, new PanelAnalysis(panel.Id, analysis.Id));
        await session.Database.SaveChangesAsync();

        Assert.Equal(HttpStatusCode.OK, (await session.Client.PutAsJsonAsync($"{Analyses}/{analysis.Id}", new { code = analysis.Code, name = "Changed catalogue", unit = "unit", price = 99 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.DeleteAsync($"{Analyses}/{analysis.Id}/reference-ranges/{range.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.DeleteAsync($"{Panels}/{panel.Id}/analyses/{analysis.Id}")).StatusCode);
        await session.Client.PutAsJsonAsync($"{Panels}/{panel.Id}", new { code = panel.Code, name = "Changed catalogue panel" });
        await session.Client.PostAsync($"{Analyses}/{analysis.Id}/deactivate", null);
        await session.Client.PostAsync($"{Panels}/{panel.Id}/deactivate", null);
        session.Database.ChangeTracker.Clear();
        var historicalItem = await session.Database.RequestItems.SingleAsync(x => x.Id == item.Id);
        Assert.Equal(10, historicalItem.UnitPriceSnapshot);
        Assert.Equal(10, historicalItem.DiscountPercentSnapshot);
        Assert.Equal(9, historicalItem.NetPrice);
        Assert.Equal("Synthetic history", historicalItem.AnalysisNameSnapshot);
        Assert.Equal("Synthetic history panel", (await session.Database.RequestPanels.SingleAsync(x => x.Id == selectedPanel.Id)).PanelNameSnapshot);
        Assert.Equal(2, (await session.Database.AnalysisResults.SingleAsync(x => x.Id == result.Id)).ReferenceUpperBoundSnapshot);
        Assert.True(await session.Database.AuditEntries.AnyAsync(x => x.Id == audit.Id));
    }

    private static async Task<AnalysisDto> CreateAnalysis(HttpClient client, string? code = null, string name = "Synthetic analysis")
    {
        var response = await client.PostAsJsonAsync(Analyses, new { code = code ?? Code(), name, unit = "unit", price = 10m });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return await Read<AnalysisDto>(response);
    }
    private static async Task<PanelDto> CreatePanel(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(Panels, new { code = Code(), name = "Synthetic panel" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return await Read<PanelDto>(response);
    }
    private static ReferenceRangeInput Range(int min, int? max, Sex sex = Sex.Female, decimal lower = 0, decimal upper = 1) => new(sex, min, max, lower, upper);
    private static string Code() => $"T{Guid.NewGuid():N}"[..25].ToUpperInvariant();
    private static async Task<T> Get<T>(HttpClient client, string route)
    {
        var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Read<T>(response);
    }
    private static async Task<T> Read<T>(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<T>(Json))!;
    private static async Task ExpectProblem(HttpResponseMessage response, HttpStatusCode status, bool validation = false)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await Read<JsonElement>(response);
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        if (validation) Assert.NotEmpty(body.GetProperty("errors").EnumerateObject());
        Assert.DoesNotContain("SqlException", body.ToString());
    }
}
