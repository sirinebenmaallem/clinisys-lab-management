using Clinisys.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Clinisys.Infrastructure.Persistence;

public sealed class ClinisysDbContext(DbContextOptions<ClinisysDbContext> options) : DbContext(options)
{
    public DbSet<LaboratoryRequestCounter> LaboratoryRequestCounters => Set<LaboratoryRequestCounter>();
    // Reuses exactly the current options/interceptors; never falls back to application configuration.
    internal ClinisysDbContext CreateIndependentContext() => new(options);
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<MedicalAnalysis> MedicalAnalyses => Set<MedicalAnalysis>();
    public DbSet<ReferenceRange> ReferenceRanges => Set<ReferenceRange>();
    public DbSet<AnalysisPanel> AnalysisPanels => Set<AnalysisPanel>();
    public DbSet<PanelAnalysis> PanelAnalyses => Set<PanelAnalysis>();
    public DbSet<LaboratoryRequest> LaboratoryRequests => Set<LaboratoryRequest>();
    public DbSet<RequestPanel> RequestPanels => Set<RequestPanel>();
    public DbSet<RequestItem> RequestItems => Set<RequestItem>();
    public DbSet<AnalysisResult> AnalysisResults => Set<AnalysisResult>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ClinisysDbContext).Assembly);
}
