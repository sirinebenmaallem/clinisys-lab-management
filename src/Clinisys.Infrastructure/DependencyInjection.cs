using Clinisys.Infrastructure.Persistence;
using Clinisys.Application.Features.LaboratoryReports;
using Clinisys.Infrastructure.LaboratoryReports;
using Clinisys.Infrastructure.Authentication;
using Clinisys.Application.Features.AnalysisResults;
using Clinisys.Infrastructure.AnalysisResults;
using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Infrastructure.LaboratoryRequests;
using Clinisys.Application.Features.Patients;
using Clinisys.Infrastructure.Patients;
using Clinisys.Infrastructure.Catalog;
using Clinisys.Application.Features.MedicalAnalyses;
using Clinisys.Application.Features.ReferenceRanges;
using Clinisys.Application.Features.AnalysisPanels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clinisys.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IPatientAnalysisHistoryStore, PatientAnalysisHistoryStore>();
        services.AddScoped<ILaboratoryReportStore, LaboratoryReportStore>();
        services.AddSingleton<ILaboratoryReportPdf>(_ => new LaboratoryReportPdf(configuration["Laboratory:Name"] ?? "Laboratoire"));
        services.AddClinisysIdentity();
        // No default actor is installed. Resolve lazily so existing modules can start;
        // result-store resolution fails closed until an authorized server provider exists.
        services.AddScoped<IAnalysisResultStore>(provider => new AnalysisResultStore(
            provider.GetRequiredService<ClinisysDbContext>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IAnalysisResultActorProvider>(), new LdlAnalysisCodes(
                configuration["LdlCalculation:TotalCholesterolCode"], configuration["LdlCalculation:HdlCode"],
                configuration["LdlCalculation:TriglyceridesCode"], configuration["LdlCalculation:CalculatedLdlCode"])));
        services.AddScoped<IRequestWorkflowStore, RequestWorkflowStore>();
        services.AddScoped<ILaboratoryRequestStore, LaboratoryRequestStore>();
        services.AddScoped<IPatientStore, PatientStore>();
        services.AddScoped<IMedicalAnalysisStore, MedicalAnalysisStore>();
        services.AddScoped<IReferenceRangeStore, ReferenceRangeStore>();
        services.AddScoped<IAnalysisPanelStore, AnalysisPanelStore>();
        // Resolve lazily so the health endpoint does not require database access/configuration.
        services.AddDbContext<ClinisysDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("ClinisysDb")
                ?? throw new InvalidOperationException("ConnectionStrings:ClinisysDb must be configured.");
            options.UseSqlServer(connectionString);
            // Never enable sensitive-data logging; connection strings must not be logged.
        });
        return services;
    }
}
