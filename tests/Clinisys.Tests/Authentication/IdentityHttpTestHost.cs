using System.Security.Claims;
using Clinisys.Infrastructure.Authentication;
using Clinisys.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Clinisys.Application.Features.Patients;
using Clinisys.Domain;
using Clinisys.Domain.Entities;
using Clinisys.Application.Features.MedicalAnalyses;
using Clinisys.Application.Features.LaboratoryRequests;
using Clinisys.Application.Common;

namespace Clinisys.Tests.Authentication;

// Test-only storage double. Production keeps the standard EF stores.
internal sealed class IdentityHttpTestHost : WebApplicationFactory<Program>
{
    internal const string Password = "Synthetic password for HTTP 07";
    internal readonly ApplicationUser Account = new() { UserName = "synthetic.user", NormalizedUserName = "SYNTHETIC.USER", IsActive = true };
    internal IList<string> Roles = new List<string> { "Secretary" };
    internal readonly Clock Time = new();
    internal string? LastPatientSearch;
    internal IdentityHttpTestHost() => Account.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(Account, Password);
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Time);
            services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, o => o.TimeProvider = Time);
            services.RemoveAll<IUserStore<ApplicationUser>>();
            services.AddScoped<IUserStore<ApplicationUser>>(_ => new Store(this));
            services.RemoveAll<IUserClaimsPrincipalFactory<ApplicationUser>>();
            services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>>(_ => new Claims(this));
            services.RemoveAll<ClinisysDbContext>();
            services.AddScoped<ClinisysDbContext>(_ => throw new InvalidOperationException("SQL forbidden in HTTP authentication tests."));
            services.RemoveAll<IPatientStore>();
            services.AddScoped<IPatientStore>(_ => new Patients(this));
            services.RemoveAll<IMedicalAnalysisStore>(); services.AddScoped<IMedicalAnalysisStore, CatalogTripwire>();
            services.RemoveAll<ILaboratoryRequestStore>(); services.AddScoped<ILaboratoryRequestStore, RequestTripwire>();
        });
    }
    internal HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    internal sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan elapsed) => now += elapsed;
    }

    private sealed class Claims(IdentityHttpTestHost host) : IUserClaimsPrincipalFactory<ApplicationUser>
    {
        public Task<ClaimsPrincipal> CreateAsync(ApplicationUser user) => Task.FromResult(new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.UserName!),
                new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp!) }
                .Concat(host.Roles.Select(r => new Claim(ClaimTypes.Role, r))), IdentityConstants.ApplicationScheme)));
    }

    private sealed class Patients(IdentityHttpTestHost host) : IPatientStore
    {
        public Task<PatientDetailDto?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<PatientDetailDto?>(
            new(id, "Synthetic", "Patient", new(2000, 1, 1), Sex.Unspecified, "SYN-CIN", "SYN-PHONE", Convert.ToBase64String(new byte[8])));
        public Task<PatientPage> SearchAsync(PatientPageQuery query, CancellationToken ct)
        {
            host.LastPatientSearch = query.Search;
            return Task.FromResult(new PatientPage([new(Guid.NewGuid(), "Synthetic", "Patient", new(2000, 1, 1), Sex.Unspecified)], 1, query.Page, query.PageSize));
        }
        public Task<PatientDetailDto> CreateAsync(Patient patient, CancellationToken ct) => throw new NotSupportedException();
        public Task<PatientDetailDto> UpdateAsync(Guid id, Patient patient, byte[] version, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class CatalogTripwire : IMedicalAnalysisStore
    {
        public Task<AnalysisDto?> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<CatalogPage<AnalysisDto>> SearchAsync(CatalogPageQuery query, CancellationToken ct) => throw new NotSupportedException();
        public Task<AnalysisDto> CreateAsync(MedicalAnalysis analysis, CancellationToken ct) => throw new NotSupportedException();
        public Task<AnalysisDto> UpdateAsync(Guid id, MedicalAnalysis analysis, CancellationToken ct) => throw new NotSupportedException();
        public Task DeactivateAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class RequestTripwire : ILaboratoryRequestStore
    {
        public Task<LaboratoryRequestDetailDto> CreateAsync(LaboratoryRequestInput input, CancellationToken ct) => throw new NotSupportedException();
        public Task<LaboratoryRequestDetailDto?> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<LaboratoryRequestPage> SearchAsync(LaboratoryRequestPageQuery query, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Store(IdentityHttpTestHost host) : IUserPasswordStore<ApplicationUser>, IUserSecurityStampStore<ApplicationUser>,
        IUserRoleStore<ApplicationUser>, IUserLockoutStore<ApplicationUser>
    {
        private static ApplicationUser Copy(ApplicationUser source)
        {
            var user = new ApplicationUser();
            foreach (var p in typeof(ApplicationUser).GetProperties().Where(p => p.CanRead && p.CanWrite)) p.SetValue(user, p.GetValue(source));
            return user;
        }
        public Task<ApplicationUser?> FindByIdAsync(string id, CancellationToken ct) => Task.FromResult(id == host.Account.Id.ToString() ? Copy(host.Account) : null);
        public Task<ApplicationUser?> FindByNameAsync(string name, CancellationToken ct) => Task.FromResult(name == host.Account.NormalizedUserName ? Copy(host.Account) : null);
        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (user.ConcurrencyStamp != host.Account.ConcurrencyStamp) return Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure" }));
            user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            foreach (var p in typeof(ApplicationUser).GetProperties().Where(p => p.CanRead && p.CanWrite)) p.SetValue(host.Account, p.GetValue(user));
            return Task.FromResult(IdentityResult.Success);
        }
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken ct) => throw new NotSupportedException();
        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> GetUserIdAsync(ApplicationUser u, CancellationToken ct) => Task.FromResult(u.Id.ToString());
        public Task<string?> GetUserNameAsync(ApplicationUser u, CancellationToken ct) => Task.FromResult(u.UserName);
        public Task SetUserNameAsync(ApplicationUser u, string? v, CancellationToken ct) { u.UserName = v; return Task.CompletedTask; }
        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser u, CancellationToken ct) => Task.FromResult(u.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(ApplicationUser u, string? v, CancellationToken ct) { u.NormalizedUserName = v; return Task.CompletedTask; }
        public Task<string?> GetPasswordHashAsync(ApplicationUser u, CancellationToken ct) => Task.FromResult(u.PasswordHash);
        public Task<bool> HasPasswordAsync(ApplicationUser u, CancellationToken ct) => Task.FromResult(u.PasswordHash != null);
        public Task SetPasswordHashAsync(ApplicationUser u, string? v, CancellationToken ct) { u.PasswordHash = v; return Task.CompletedTask; }
        public Task<string?> GetSecurityStampAsync(ApplicationUser u, CancellationToken ct) => Task.FromResult(u.SecurityStamp);
        public Task SetSecurityStampAsync(ApplicationUser u, string v, CancellationToken ct) { u.SecurityStamp = v; return Task.CompletedTask; }
        public Task<IList<string>> GetRolesAsync(ApplicationUser u, CancellationToken ct) => Task.FromResult<IList<string>>(host.Roles.ToArray());
        public Task<bool> IsInRoleAsync(ApplicationUser u, string role, CancellationToken ct) => Task.FromResult(host.Roles.Any(r => r.ToUpperInvariant() == role));
        public Task<IList<ApplicationUser>> GetUsersInRoleAsync(string role, CancellationToken ct) => throw new NotSupportedException();
        public Task AddToRoleAsync(ApplicationUser u, string role, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveFromRoleAsync(ApplicationUser u, string role, CancellationToken ct) => throw new NotSupportedException();
        public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser u, CancellationToken ct) => Task.FromResult(u.LockoutEnd);
        public Task SetLockoutEndDateAsync(ApplicationUser u, DateTimeOffset? end, CancellationToken ct) { u.LockoutEnd = end; return Task.CompletedTask; }
        public Task<int> IncrementAccessFailedCountAsync(ApplicationUser u, CancellationToken ct) => Task.FromResult(++u.AccessFailedCount);
        public Task ResetAccessFailedCountAsync(ApplicationUser u, CancellationToken ct) { u.AccessFailedCount = 0; return Task.CompletedTask; }
        public Task<int> GetAccessFailedCountAsync(ApplicationUser u, CancellationToken ct) => Task.FromResult(u.AccessFailedCount);
        public Task<bool> GetLockoutEnabledAsync(ApplicationUser u, CancellationToken ct) => Task.FromResult(u.LockoutEnabled);
        public Task SetLockoutEnabledAsync(ApplicationUser u, bool v, CancellationToken ct) { u.LockoutEnabled = v; return Task.CompletedTask; }
        public void Dispose() { }
    }
}
