using Microsoft.AspNetCore.Identity;

namespace Clinisys.Infrastructure.Authentication;

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole()
    {
        Id = Guid.NewGuid();
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }
}
