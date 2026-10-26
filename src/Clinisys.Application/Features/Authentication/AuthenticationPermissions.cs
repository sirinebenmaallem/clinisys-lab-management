namespace Clinisys.Application.Features.Authentication;

// Pure role policy, not a substitute for authentication or server-side authorization.
public static class AuthenticationPermissions
{
    public const string CatalogRead = "Catalog.Read";
    public const string CatalogWrite = "Catalog.Write";
    public const string PatientsSearch = "Patients.Search";
    public const string PatientsReadDetail = "Patients.ReadDetail";
    public const string PatientsWrite = "Patients.Write";
    public const string RequestsRead = "Requests.Read";
    public const string RequestsCreate = "Requests.Create";
    public const string RequestsProcess = "Requests.Process";
    public const string RequestsValidate = "Requests.Validate";
    public const string RequestsDeliver = "Requests.Deliver";
    public const string ResultsRead = "Results.Read";
    public const string ResultsWrite = "Results.Write";
    public const string DashboardRead = "Dashboard.Read";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        CatalogRead, CatalogWrite, PatientsSearch, PatientsReadDetail, PatientsWrite,
        RequestsRead, RequestsCreate, RequestsProcess, RequestsValidate, RequestsDeliver, ResultsRead, ResultsWrite, DashboardRead
    });

    public static IReadOnlyList<string> ForRoles(IEnumerable<string>? roles)
    {
        var permissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles ?? [])
        {
            switch (role)
            {
                case AuthenticationRoles.Secretary:
                    permissions.UnionWith([CatalogRead, PatientsSearch, PatientsReadDetail,
                        PatientsWrite, RequestsRead, RequestsCreate, RequestsDeliver]);
                    break;
                case AuthenticationRoles.Technician:
                    permissions.UnionWith([CatalogRead, PatientsSearch, RequestsRead, RequestsProcess, ResultsRead, ResultsWrite]);
                    break;
                case AuthenticationRoles.Biologist:
                    permissions.UnionWith([CatalogRead, CatalogWrite, PatientsSearch, RequestsRead, RequestsProcess, RequestsValidate, RequestsDeliver, ResultsRead, ResultsWrite, DashboardRead]);
                    break;
            }
        }
        return Array.AsReadOnly(permissions.Order(StringComparer.Ordinal).ToArray());
    }
}
