using Microsoft.AspNetCore.Identity;

namespace ErrandRuns.Infrastructure.Identity;

/// <summary>
/// The single login account for both customers and runners.
/// A role determines which API operations the account can perform.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser(string displayName, string email, string? phoneNumber = null)
    {
        Id = Guid.NewGuid();
        DisplayName = displayName.Trim();
        UserName = email.Trim();
        Email = email.Trim();
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
    }

    // EF Core needs a parameterless constructor when materialising an account.
    private ApplicationUser() { DisplayName = string.Empty; }

    public string DisplayName { get; private set; }
    public string? Bio { get; private set; }

    public void UpdateProfile(string displayName, string? phoneNumber, string? bio)
    {
        DisplayName = displayName.Trim();
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        Bio = string.IsNullOrWhiteSpace(bio) ? null : bio.Trim();
    }
}
