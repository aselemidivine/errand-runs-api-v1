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
        var identifier = request.EmailOrPhone ?? request.Email;
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(request.Password))
            return AuthenticationResult.Failure("Email or phone and password are required.");

        identifier = identifier.Trim();
        var user = identifier.Contains('@')
            ? await users.FindByEmailAsync(identifier)
            : await users.Users.SingleOrDefaultAsync(value => value.PhoneNumber == identifier, ct);
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

    public async Task<AuthenticationResult> UpdateAccount(Guid userId, UpdateAccount request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return AuthenticationResult.Failure("Account was not found.");
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return AuthenticationResult.Failure("Display name is required.");
        if (request.DisplayName.Trim().Length > 120)
            return AuthenticationResult.Failure("Display name cannot exceed 120 characters.");
        if (request.Bio?.Trim().Length > 300)
            return AuthenticationResult.Failure("Bio cannot exceed 300 characters.");

        var phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        if (phone is not null && await users.Users.AnyAsync(value => value.Id != userId && value.PhoneNumber == phone, ct))
            return AuthenticationResult.Failure("Phone number is already in use.");

        var phoneChanged = !string.Equals(user.PhoneNumber, phone, StringComparison.Ordinal);
        user.UpdateProfile(request.DisplayName, phone, request.Bio);
        if (phoneChanged) user.PhoneNumberConfirmed = false;

        var result = await users.UpdateAsync(user);
        return result.Succeeded
            ? AuthenticationResult.Success(await ToAccount(user, ct))
            : Failure(result);
    }

    public async Task<PasswordResetTicket?> CreatePasswordReset(ForgotPassword request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email)) return null;
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null) return null;
        return new PasswordResetTicket(await users.GeneratePasswordResetTokenAsync(user));
    }

    public async Task<AuthenticationResult> ResetPassword(ResetPassword request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
            return AuthenticationResult.Failure("Email, reset token, and new password are required.");
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null) return AuthenticationResult.Failure("The password reset request is invalid.");
        var result = await users.ResetPasswordAsync(user, request.Token, request.NewPassword);
        return result.Succeeded
            ? AuthenticationResult.Success(await ToAccount(user, ct))
            : Failure(result);
    }

    private async Task<AuthenticationResult> Register(RegisterAccount request, string role, bool createRunnerProfile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return AuthenticationResult.Failure("Display name, email, and password are required.");

        var roleResult = await EnsureRole(role);
        if (!roleResult.Succeeded) return Failure(roleResult);

        var phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        if (phone is not null && await users.Users.AnyAsync(value => value.PhoneNumber == phone, ct))
            return AuthenticationResult.Failure("Phone number is already in use.");

        var user = new ApplicationUser(request.DisplayName, request.Email, phone);
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
        return new AccountDetails(
            user.Id,
            user.DisplayName,
            user.Email ?? string.Empty,
            user.PhoneNumber,
            user.EmailConfirmed,
            user.PhoneNumberConfirmed,
            user.Bio,
            role,
            runnerStatus);
    }

    private static AuthenticationResult Failure(IdentityResult result) =>
        AuthenticationResult.Failure(result.Errors.Select(error => error.Description).ToArray());
}
