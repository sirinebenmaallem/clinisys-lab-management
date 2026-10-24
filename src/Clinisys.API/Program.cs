using Clinisys.Infrastructure;
using Clinisys.API.Errors;
using Clinisys.Application.Features.MedicalAnalyses;
using Clinisys.Application.Features.ReferenceRanges;
using Clinisys.Application.Features.AnalysisPanels;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CatalogExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<MedicalAnalysisCommands>();
builder.Services.AddScoped<MedicalAnalysisQueries>();
builder.Services.AddScoped<ReferenceRangeCommands>();
builder.Services.AddScoped<ReferenceRangeQueries>();
builder.Services.AddScoped<AnalysisPanelCommands>();
builder.Services.AddScoped<AnalysisPanelQueries>();

var app = builder.Build();
app.UseExceptionHandler(new ExceptionHandlerOptions { SuppressDiagnosticsCallback = _ => true });
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();

// Expose the entry point to the integration test host.
public partial class Program;
