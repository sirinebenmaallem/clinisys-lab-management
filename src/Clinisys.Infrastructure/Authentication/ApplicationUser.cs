using Microsoft.AspNetCore.Identity;

namespace Clinisys.Infrastructure.Authentication;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.NewGuid();
        SecurityStamp = Guid.NewGuid().ToString("N");
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
        LockoutEnabled = true;
    }

    // Provisioning must explicitly activate after assigning approved roles.
    public bool IsActive { get; set; }
}
