using ErrandRuns.Application;
using ErrandRuns.Domain.Runners;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ErrandRuns.Infrastructure.Identity;

/// <summary>
/// Adapts ASP.NET Core Identity to the application's authentication contracts.
/// Password hashing and credential verification are deliberately delegated to
/// Identity; passwords are never stored or logged by application code.
/// </summary>
public sealed class IdentityAuthenticationService(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    ErrandRunsDbContext db) : IAuthenticationService
{
    public Task<AuthenticationResult> RegisterCustomer(RegisterAccount request, CancellationToken ct) =>
        Register(request, "Customer", createRunnerProfile: false, ct);

    public Task<AuthenticationResult> RegisterRunner(RegisterAccount request, CancellationToken ct) =>
        Register(request, "Runner", createRunnerProfile: true, ct);

    public async Task<AuthenticationResult> ValidateCredentials(Login request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !await users.CheckPasswordAsync(user, request.Password))
            return AuthenticationResult.Failure("Email or password is incorrect.");

        return AuthenticationResult.Success(await ToAccount(user, ct));
    }

    public async Task<AuthenticationResult> ChangePassword(Guid userId, ChangePassword request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return AuthenticationResult.Failure("Account was not found.");

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        return result.Succeeded
            ? AuthenticationResult.Success(await ToAccount(user, ct))
            : Failure(result);
    }

    public async Task<AccountDetails?> GetAccount(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null ? null : await ToAccount(user, ct);
    }

    private async Task<AuthenticationResult> Register(RegisterAccount request, string role, bool createRunnerProfile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return AuthenticationResult.Failure("Display name, email, and password are required.");

        var roleResult = await EnsureRole(role);
        if (!roleResult.Succeeded) return Failure(roleResult);

        var user = new ApplicationUser(request.DisplayName, request.Email);
        var createResult = await users.CreateAsync(user, request.Password);
        if (!createResult.Succeeded) return Failure(createResult);

        var addRoleResult = await users.AddToRoleAsync(user, role);
        if (!addRoleResult.Succeeded) return Failure(addRoleResult);

        // Runner workflows rely on a profile. New runners start as Applicants
        // and cannot be matched until the operational verification flow approves them.
        if (createRunnerProfile)
        {
            db.Runners.Add(new RunnerProfile(user.Id));
            await db.SaveChangesAsync(ct);
        }

        return AuthenticationResult.Success(await ToAccount(user, ct));
    }

    private async Task<IdentityResult> EnsureRole(string role)
    {
        if (await roles.RoleExistsAsync(role)) return IdentityResult.Success;
        return await roles.CreateAsync(new IdentityRole<Guid>(role));
    }

    private async Task<AccountDetails> ToAccount(ApplicationUser user, CancellationToken ct)
    {
        var rolesForUser = await users.GetRolesAsync(user);
        var role = rolesForUser.SingleOrDefault() ?? throw new InvalidOperationException("Account has no role.");
        var runnerStatus = role == "Runner"
            ? await db.Runners.AsNoTracking().Where(r => r.UserId == user.Id).Select(r => (RunnerStatus?)r.Status).SingleOrDefaultAsync(ct)
            : null;
        return new AccountDetails(user.Id, user.DisplayName, user.Email ?? string.Empty, role, runnerStatus);
    }

    private static AuthenticationResult Failure(IdentityResult result) =>
        AuthenticationResult.Failure(result.Errors.Select(error => error.Description).ToArray());
}
