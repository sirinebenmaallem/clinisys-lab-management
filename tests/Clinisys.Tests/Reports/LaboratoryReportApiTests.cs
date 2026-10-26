using System.Net;
using Clinisys.Application.Features.LaboratoryReports;
using Clinisys.Domain;
using Clinisys.Infrastructure.Persistence;
using Clinisys.Tests.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PdfSharp.Pdf.IO;

namespace Clinisys.Tests.Reports;

public sealed class LaboratoryReportApiTests
{
    [Theory]
    [InlineData("Secretary", RequestStatus.Validated)]
    [InlineData("Secretary", RequestStatus.Delivered)]
    [InlineData("Technician", RequestStatus.Validated)]
    [InlineData("Technician", RequestStatus.Delivered)]
    [InlineData("Biologist", RequestStatus.Validated)]
    [InlineData("Biologist", RequestStatus.Delivered)]
    public async Task AuthorizedRole_DownloadsRealPdf_NoCacheOrMutation(string role, RequestStatus status)
    {
        using var host = new Host(role, status);
        using var client = host.Client();
        var before = host.Data;
        using var response = await client.GetAsync(host.Route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("LAB-2026-000001", response.Content.Headers.ContentDisposition.FileName);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var stream = new MemoryStream(bytes);
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount > 0);
        Assert.Equal(before, host.Data);
        Assert.Equal(1, host.Reads);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("Unknown", 403)]
    public async Task UnauthorizedCall_DoesNotResolveReadStore(string? role, int expected)
    {
        using var host = new Host(role);
        using var client = host.Client();
        using var response = await client.GetAsync(host.Route);
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(0, host.Reads);
    }

    [Theory]
    [InlineData(RequestStatus.Registered)]
    [InlineData(RequestStatus.InProgress)]
    [InlineData(RequestStatus.ResultsEntered)]
    public async Task UnvalidatedRequest_Returns409AndNoPatientData(RequestStatus status)
    {
        using var host = new Host("Secretary", status);
        using var client = host.Client();
        using var response = await client.GetAsync(host.Route);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Synthétique", body);
        Assert.DoesNotContain(host.Data!.PatientId.ToString(), body);
    }

    [Fact]
    public async Task MissingRequest404AndUnexpectedFailureAreSanitized()
    {
        using var host = new Host("Biologist");
        using var client = host.Client();
        host.Data = null;
        using var missing = await client.GetAsync(host.Route);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        host.Failure = new InvalidOperationException("SYN-PRIVATE patient/password/server");
        using var failed = await client.GetAsync(host.Route);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.DoesNotContain("SYN-PRIVATE", await failed.Content.ReadAsStringAsync());
        Assert.True(failed.Headers.CacheControl?.NoStore);
    }

    private sealed class Host(string? role, RequestStatus status = RequestStatus.Validated) : WebApplicationFactory<Program>
    {
        public LaboratoryReportData? Data = LaboratoryReportTests.Report(status);
        public Exception? Failure;
        public int Reads;
        public string Route => "/api/laboratory-requests/10000000-0000-0000-0000-000000000010/report";
        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.AddTestAuthentication(role is null ? [] : [role]);
                services.RemoveAll<ILaboratoryReportStore>();
                services.AddScoped<ILaboratoryReportStore>(_ => new Store(this));
                services.RemoveAll<ClinisysDbContext>();
                services.AddScoped<ClinisysDbContext>(_ => throw new InvalidOperationException("SQL forbidden in PDF HTTP tests."));
            });
        }
        private sealed class Store(Host host) : ILaboratoryReportStore
        {
            public Task<LaboratoryReportData?> GetAsync(Guid id, CancellationToken ct)
            {
                host.Reads++;
                if (host.Failure is not null) throw host.Failure;
                return Task.FromResult(host.Data);
            }
        }
    }
}
