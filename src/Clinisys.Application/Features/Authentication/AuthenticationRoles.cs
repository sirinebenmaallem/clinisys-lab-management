namespace Clinisys.Application.Features.Authentication;

public static class AuthenticationRoles
{
    public const string Secretary = "Secretary";
    public const string Technician = "Technician";
    public const string Biologist = "Biologist";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Secretary, Technician, Biologist });

    public static bool IsKnown(string? role) => role is Secretary or Technician or Biologist;
}
