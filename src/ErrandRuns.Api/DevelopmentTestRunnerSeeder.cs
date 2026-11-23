using ErrandRuns.Domain.Runners;
using ErrandRuns.Infrastructure;
using ErrandRuns.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ErrandRuns.Api;

public static class DevelopmentTestRunnerSeeder
{
    public static async Task VerifyConfiguredRunner(
        IServiceProvider services, IConfiguration configuration, Serilog.ILogger logger)
    {
        var email = configuration["Testing:AutoVerifyRunnerEmail"]?.Trim();
        if (string.IsNullOrWhiteSpace(email)) return;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ErrandRunsDbContext>();
        if (db.Database.IsInMemory()) return;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            logger.Warning("Development auto-verification skipped: runner {Email} was not found.", email);
            return;
        }
        if (!await users.IsInRoleAsync(user, "Runner"))
        {
            logger.Warning("Development auto-verification skipped: account {Email} is not a Runner.", email);
            return;
        }

        user.EmailConfirmed = true;
        if (!string.IsNullOrWhiteSpace(user.PhoneNumber)) user.PhoneNumberConfirmed = true;
        var update = await users.UpdateAsync(user);
        if (!update.Succeeded)
            throw new InvalidOperationException("Could not confirm the configured development runner account.");

        var profile = await db.Runners.SingleOrDefaultAsync(x => x.UserId == user.Id)
            ?? throw new InvalidOperationException("Configured development runner has no runner profile.");
        if (profile.Status == RunnerStatus.Applicant) profile.SubmitVerification();
        if (profile.Status == RunnerStatus.PendingVerification) profile.Approve();

        var verification = await db.RunnerVerifications.SingleOrDefaultAsync(x => x.RunnerId == user.Id);
        if (verification is null)
        {
            verification = new RunnerVerification(user.Id);
            db.RunnerVerifications.Add(verification);
        }
        if (verification.Status is VerificationStatus.Draft or VerificationStatus.Rejected)
        {
            verification.SetIdentity(GovernmentIdType.NationalIdCard,
                "development-seed/identity-not-uploaded");
            verification.SetVehicle(RunnerVehicleType.Walking, null, null, null);
            verification.Submit(DateTimeOffset.UtcNow);
        }
        if (verification.Status is VerificationStatus.Submitted or VerificationStatus.UnderReview)
            verification.Approve(DateTimeOffset.UtcNow);

        await db.SaveChangesAsync();
        logger.Information("Development runner {Email} was automatically verified.", email);
    }
}
