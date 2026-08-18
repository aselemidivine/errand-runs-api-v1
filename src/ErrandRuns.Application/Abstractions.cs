using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Errands;
using ErrandRuns.Domain.Runners;
namespace ErrandRuns.Application;

public interface IErrandRepository { Task Add(Errand value, CancellationToken ct); Task<Errand?> Find(Guid id, CancellationToken ct); Task<IReadOnlyList<Errand>> ListForUser(Guid userId, bool runner, int skip, int take, CancellationToken ct); Task Save(CancellationToken ct); }
public interface IRunnerRepository { Task<RunnerProfile?> Find(Guid id, CancellationToken ct); Task<IReadOnlyList<RunnerProfile>> Available(CancellationToken ct); Task Save(CancellationToken ct); }
public interface IPricingService { Money Estimate(int stopCount, decimal membershipDiscountPercent); }
public interface IRunnerMatchingService { Task<Guid?> FindRunner(Errand errand, CancellationToken ct); }
public interface IPaymentGateway { Task<PaymentIntent> CreateIntent(Guid paymentId, Money amount, CancellationToken ct); Task<bool> Verify(string providerReference, CancellationToken ct); }
public interface ICurrentUser { Guid UserId { get; } bool IsInRole(string role); }
public interface IClock { DateTimeOffset UtcNow { get; } }

// Authentication contracts live in the application layer so the API does not
// depend directly on the chosen identity provider or persistence technology.
public sealed record RegisterAccount(string DisplayName, string Email, string Password, string? PhoneNumber = null);
public sealed record Login(string? EmailOrPhone, string Password, string? Email = null);
public sealed record ChangePassword(string CurrentPassword, string NewPassword);
public sealed record UpdateAccount(string DisplayName, string? PhoneNumber, string? Bio);
public sealed record ForgotPassword(string Email);
public sealed record ResetPassword(string Email, string Token, string NewPassword);
public sealed record PasswordResetTicket(string Token);
public sealed record AccountDetails(
    Guid Id,
    string DisplayName,
    string Email,
    string? PhoneNumber,
    bool EmailConfirmed,
    bool PhoneNumberConfirmed,
    string? Bio,
    string Role,
    RunnerStatus? RunnerStatus);
public sealed record AuthenticationResult(AccountDetails? Account, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Account is not null && Errors.Count == 0;
    public static AuthenticationResult Success(AccountDetails account) => new(account, new Dictionary<string, string[]>());
    public static AuthenticationResult Failure(params string[] errors) => new(null, new Dictionary<string, string[]> { ["auth"] = errors });
}
public interface IAuthenticationService
{
    Task<AuthenticationResult> RegisterCustomer(RegisterAccount request, CancellationToken ct);
    Task<AuthenticationResult> RegisterRunner(RegisterAccount request, CancellationToken ct);
    Task<AuthenticationResult> ValidateCredentials(Login request, CancellationToken ct);
    Task<AuthenticationResult> ChangePassword(Guid userId, ChangePassword request, CancellationToken ct);
    Task<AccountDetails?> GetAccount(Guid userId, CancellationToken ct);
    Task<AuthenticationResult> UpdateAccount(Guid userId, UpdateAccount request, CancellationToken ct);
    Task<PasswordResetTicket?> CreatePasswordReset(ForgotPassword request, CancellationToken ct);
    Task<AuthenticationResult> ResetPassword(ResetPassword request, CancellationToken ct);
}
public sealed record PaymentIntent(string Reference, string CheckoutUrl);
public sealed record CreateStop(int Sequence, StopType Type, string Address, decimal Latitude, decimal Longitude, string? Instructions);
public sealed record CreateErrand(string Title, ErrandCategory Category, DateTimeOffset? ScheduledFor, IReadOnlyList<CreateStop> Stops);
public sealed record ErrandSummary(Guid Id, string Title, ErrandStatus Status, int StopCount, Guid? RunnerId);
public sealed class PricingService : IPricingService
{
    private const decimal BaseFee = 2500m, AdditionalStopFee = 750m;
    public Money Estimate(int stopCount, decimal discount) { if (stopCount < 2) throw new DomainException("At least two stops are required."); var subtotal = BaseFee + (stopCount - 2) * AdditionalStopFee; return new(subtotal * (1 - Math.Clamp(discount, 0, 100) / 100)); }
}
public sealed class RunnerMatchingService(IRunnerRepository runners) : IRunnerMatchingService
{
    public async Task<Guid?> FindRunner(Errand errand, CancellationToken ct) => (await runners.Available(ct)).OrderByDescending(x => x.Rating).ThenByDescending(x => x.CompletedErrands).Select(x => (Guid?)x.UserId).FirstOrDefault();
}
public sealed class ErrandService(IErrandRepository errands, IRunnerMatchingService matching, ICurrentUser current, IClock clock)
{
    public async Task<ErrandSummary> Create(CreateErrand command, CancellationToken ct)
    {
        if (!current.IsInRole("Customer")) throw new UnauthorizedAccessException();
        var errand = new Errand(Guid.NewGuid(), current.UserId, command.Title, command.Category, command.ScheduledFor);
        foreach (var s in command.Stops.OrderBy(x => x.Sequence)) errand.AddStop(new(Guid.NewGuid(), s.Sequence, s.Type, s.Address, new(s.Latitude, s.Longitude), s.Instructions));
        errand.RequestEstimate(); await errands.Add(errand, ct); await errands.Save(ct); return Map(errand);
    }
    public async Task<ErrandSummary> Match(Guid id, CancellationToken ct)
    {
        var e = await Owned(id, false, ct); e.BeginMatching(); var runner = await matching.FindRunner(e, ct) ?? throw new DomainException("No eligible runner is currently available."); e.AssignRunner(runner); await errands.Save(ct); return Map(e);
    }
    public async Task<ErrandSummary> Accept(Guid id, CancellationToken ct) { var e = await Owned(id, true, ct); e.Accept(current.UserId); await errands.Save(ct); return Map(e); }
    public async Task<ErrandSummary> StartStop(Guid id, Guid stopId, CancellationToken ct) { var e = await Owned(id, true, ct); e.StartStop(current.UserId, stopId); await errands.Save(ct); return Map(e); }
    public async Task<ErrandSummary> CompleteStop(Guid id, Guid stopId, CancellationToken ct) { var e = await Owned(id, true, ct); e.CompleteStop(current.UserId, stopId, clock.UtcNow); await errands.Save(ct); return Map(e); }
    private async Task<Errand> Owned(Guid id, bool runner, CancellationToken ct) { var e = await errands.Find(id, ct) ?? throw new KeyNotFoundException("Errand not found."); var owner = runner ? e.RunnerId : e.CustomerId; if (owner != current.UserId) throw new UnauthorizedAccessException(); return e; }
    private static ErrandSummary Map(Errand e) => new(e.Id, e.Title, e.Status, e.Stops.Count, e.RunnerId);
}
