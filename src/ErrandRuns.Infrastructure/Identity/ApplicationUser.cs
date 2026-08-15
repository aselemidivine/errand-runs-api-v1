using Microsoft.AspNetCore.Identity;

namespace ErrandRuns.Infrastructure.Identity;

/// <summary>
/// The single login account for both customers and runners.
/// A role determines which API operations the account can perform.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser(string displayName, string email)
    {
        Id = Guid.NewGuid();
        DisplayName = displayName.Trim();
        UserName = email.Trim();
        Email = email.Trim();
    }

    // EF Core needs a parameterless constructor when materialising an account.
    private ApplicationUser() { DisplayName = string.Empty; }

    public string DisplayName { get; private set; }
}
