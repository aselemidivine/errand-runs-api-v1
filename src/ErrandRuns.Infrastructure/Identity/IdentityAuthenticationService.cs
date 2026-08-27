using ErrandRuns.Application;
using ErrandRuns.Domain.Runners;
using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Users;
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
    ErrandRunsDbContext db,
    IPhoneOtpSender otpSender,
    IClock clock) : IAuthenticationService
{
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
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

        var phone = NormalizePhone(request.PhoneNumber);
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

    public async Task<PhoneVerificationChallengeDetails> RequestPhoneVerification(
        Guid userId, bool includeDevelopmentCode, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw new KeyNotFoundException("Account was not found.");
        var phone = NormalizePhone(user.PhoneNumber)
            ?? throw new DomainException("Add a valid phone number before requesting verification.");
        if (user.PhoneNumberConfirmed)
            throw new DomainException("The phone number is already verified.");

        var now = clock.UtcNow;
        var challenge = await db.PhoneVerificationChallenges
            .Where(x => x.UserId == userId && x.ConsumedAt == null)
            .OrderByDescending(x => x.SentAt)
            .FirstOrDefaultAsync(ct);

        if (challenge is not null && !challenge.CanResend(now, ResendCooldown))
        {
            var seconds = Math.Max(1, (int)Math.Ceiling((challenge.SentAt + ResendCooldown - now).TotalSeconds));
            throw new DomainException($"Wait {seconds} seconds before requesting another code.");
        }

        var code = await users.GenerateChangePhoneNumberTokenAsync(user, phone);
        var expiresAt = now + OtpLifetime;
        if (challenge is null || !string.Equals(challenge.PhoneNumber, phone, StringComparison.Ordinal))
        {
            challenge = new PhoneVerificationChallenge(Guid.NewGuid(), userId, phone, now, expiresAt);
            db.PhoneVerificationChallenges.Add(challenge);
        }
        else
        {
            challenge.Resent(now, expiresAt);
        }

        await db.SaveChangesAsync(ct);
        try
        {
            await otpSender.Send(phone, code, ct);
        }
        catch
        {
            // Do not make the user wait through the resend cooldown when the
            // provider rejected or failed to deliver the request.
            challenge.DeliveryFailed(clock.UtcNow, ResendCooldown);
            await db.SaveChangesAsync(ct);
            throw;
        }

        return new PhoneVerificationChallengeDetails(challenge.Id, Mask(phone), expiresAt,
            (int)ResendCooldown.TotalSeconds, includeDevelopmentCode ? code : null);
    }

    public async Task<AuthenticationResult> VerifyPhoneNumber(
        Guid userId, VerifyPhoneNumber request, CancellationToken ct)
    {
        if (request.Code?.Length != 6 || request.Code.Any(character => !char.IsDigit(character)))
            return AuthenticationResult.Failure("Enter the six-digit verification code.");

        var challenge = await db.PhoneVerificationChallenges
            .SingleOrDefaultAsync(x => x.Id == request.ChallengeId && x.UserId == userId, ct);
        if (challenge is null || !challenge.IsUsable(clock.UtcNow))
            return AuthenticationResult.Failure("The verification code is expired or no longer valid.");

        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return AuthenticationResult.Failure("Account was not found.");

        var result = await users.ChangePhoneNumberAsync(user, challenge.PhoneNumber, request.Code);
        if (!result.Succeeded)
        {
            challenge.RecordFailure();
            await db.SaveChangesAsync(ct);
            return AuthenticationResult.Failure(
                challenge.FailedAttempts >= 5
                    ? "Too many incorrect attempts. Request a new code."
                    : "The verification code is incorrect.");
        }

        challenge.Consume(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return AuthenticationResult.Success(await ToAccount(user, ct));
    }

    private async Task<AuthenticationResult> Register(RegisterAccount request, string role, bool createRunnerProfile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return AuthenticationResult.Failure("Display name, email, and password are required.");

        var roleResult = await EnsureRole(role);
        if (!roleResult.Succeeded) return Failure(roleResult);

        var phone = NormalizePhone(request.PhoneNumber);
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

    private static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var compact = new string(value.Where(character => char.IsDigit(character) || character == '+').ToArray());
        if (compact.StartsWith("0", StringComparison.Ordinal) && compact.Length == 11)
            compact = "+234" + compact[1..];
        else if (compact.StartsWith("234", StringComparison.Ordinal))
            compact = "+" + compact;
        if (!compact.StartsWith('+') || compact.Length is < 9 or > 16 || compact[1..].Any(character => !char.IsDigit(character)))
            throw new DomainException("Phone number must be a valid international number, for example +2348012345678.");
        return compact;
    }

    private static string Mask(string phone)
    {
        var visible = phone.Length >= 4 ? phone[^4..] : phone;
        return $"{phone[..Math.Min(4, phone.Length)]} ••• ••• {visible}";
    }
}
