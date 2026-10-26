using Clinisys.Infrastructure;
using Clinisys.Application.Features.LaboratoryReports;
using Clinisys.API.AnalysisResults;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Application.Features.Patients;
using Microsoft.AspNetCore.Mvc;
using Clinisys.API.Errors;
using Clinisys.Application.Features.MedicalAnalyses;
using Clinisys.Application.Features.ReferenceRanges;
using Clinisys.Application.Features.AnalysisPanels;
using System.Text.Json.Serialization;
using Clinisys.API.Authentication;
using Clinisys.Application.Features.Dashboard;

var builder = WebApplication.CreateBuilder(args.Where(a => a != "--manage-users").ToArray());

// Hosting request-start/finish logs include query strings. Never record patient searches.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
// SQL duplicate-key exception text can contain the conflicting patient identifier,
// even with sensitive-data logging disabled. Keep only our sanitized error logs.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Update", LogLevel.None);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);

builder.Services.AddControllers(options => options.Filters.AddService<CsrfFilter>()).AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    var defaultFactory = options.InvalidModelStateResponseFactory;
    options.InvalidModelStateResponseFactory = context =>
    {
        if (context.HttpContext.Request.Path.StartsWithSegments("/api/auth"))
            return new BadRequestObjectResult(new ValidationProblemDetails(new Dictionary<string, string[]>
            { ["authentication"] = ["Authentication input is missing or invalid."] }) { Status = 400 });
        var isRequest = context.HttpContext.Request.Path.StartsWithSegments("/api/laboratory-requests");
        var isDashboard = context.HttpContext.Request.Path.StartsWithSegments("/api/dashboard");
        if (!isRequest && !context.HttpContext.Request.Path.StartsWithSegments("/api/patients")
            && !isDashboard) return defaultFactory(context);
        // Binding exception messages can contain submitted values; expose only fixed messages.
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            [isDashboard ? "period" : isRequest ? "request" : "patient"] = [isDashboard ? "Dashboard period is missing or invalid." : isRequest ? "Request input or query is missing or invalid." : "Patient input or query is missing or invalid."]
        }) { Status = 400, Title = "One or more validation errors occurred." };
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        var result = new BadRequestObjectResult(problem);
        result.ContentTypes.Add("application/problem+json");
        return result;
    };
});
builder.Services.AddExceptionHandler<PatientExceptionHandler>();
builder.Services.AddExceptionHandler<AuthenticationExceptionHandler>();
builder.Services.AddExceptionHandler<AnalysisResultExceptionHandler>();
builder.Services.AddExceptionHandler<LaboratoryRequestExceptionHandler>();
builder.Services.AddExceptionHandler<DashboardExceptionHandler>();
builder.Services.AddExceptionHandler<CatalogExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpIdentity(builder.Configuration, builder.Environment);
builder.Services.AddScoped<ResultErrorScopeFilter>();
builder.Services.AddScoped<IAnalysisResultActorProvider, AuthenticatedAnalysisResultActorProvider>();
builder.Services.AddScoped<IRequestWorkflowActorProvider, AuthenticatedRequestWorkflowActorProvider>();
builder.Services.AddScoped<AnalysisResultCommands>();
builder.Services.AddScoped<AnalysisResultQueries>();
builder.Services.AddScoped<PatientAnalysisHistoryQueries>();
builder.Services.AddScoped<DashboardQueries>();
builder.Services.AddScoped<LaboratoryReportQueries>();
builder.Services.AddScoped<RequestWorkflowCommands>();
builder.Services.AddScoped<LaboratoryRequestCommands>();
builder.Services.AddScoped<LaboratoryRequestQueries>();
builder.Services.AddScoped<PatientCommands>();
builder.Services.AddScoped<PatientQueries>();
builder.Services.AddScoped<MedicalAnalysisCommands>();
builder.Services.AddScoped<MedicalAnalysisQueries>();
builder.Services.AddScoped<ReferenceRangeCommands>();
builder.Services.AddScoped<ReferenceRangeQueries>();
builder.Services.AddScoped<AnalysisPanelCommands>();
builder.Services.AddScoped<AnalysisPanelQueries>();

var app = builder.Build();
if (args.Contains("--manage-users", StringComparer.Ordinal))
{
    await LocalUserAdministration.RunAsync(app.Services);
    return;
}
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });
    await next(context);
});
app.UseExceptionHandler(new ExceptionHandlerOptions { SuppressDiagnosticsCallback = _ => true });
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
// Public login assets; every business API retains its authentication/policy checks.
var frontendRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (Directory.Exists(frontendRoot))
{
    var files = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(frontendRoot);
    app.Lifetime.ApplicationStopped.Register(files.Dispose);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = files,
        OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
    });
}
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Expose the entry point to the integration test host.
public partial class Program;
