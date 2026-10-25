using Clinisys.Infrastructure;
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
        if (!isRequest && !context.HttpContext.Request.Path.StartsWithSegments("/api/patients")) return defaultFactory(context);
        // Binding exception messages can contain submitted values; expose only fixed messages.
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            [isRequest ? "request" : "patient"] = [isRequest ? "Request input or query is missing or invalid." : "Patient input or query is missing or invalid."]
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
builder.Services.AddExceptionHandler<CatalogExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpIdentity(builder.Configuration, builder.Environment);
builder.Services.AddScoped<ResultErrorScopeFilter>();
builder.Services.AddScoped<IAnalysisResultActorProvider, AuthenticatedAnalysisResultActorProvider>();
builder.Services.AddScoped<AnalysisResultCommands>();
builder.Services.AddScoped<AnalysisResultQueries>();
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
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Expose the entry point to the integration test host.
public partial class Program;
