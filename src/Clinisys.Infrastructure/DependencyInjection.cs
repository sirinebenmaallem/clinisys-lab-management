using Clinisys.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clinisys.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
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
