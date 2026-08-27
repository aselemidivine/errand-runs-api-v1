using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Communications;
using ErrandRuns.Domain.Errands;
using ErrandRuns.Domain.Payments;
using ErrandRuns.Domain.Runners;
using ErrandRuns.Domain.Users;
namespace ErrandRuns.Application;

public interface IErrandRepository
{
    Task Add(Errand value, CancellationToken ct);
    Task<Errand?> Find(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Errand>> ListForUser(Guid userId, bool runner, bool? active, int skip, int take, CancellationToken ct);
    Task<int> CountForUser(Guid userId, bool runner, bool? active, CancellationToken ct);
    Task Save(CancellationToken ct);
}
public interface IRunnerRepository { Task<RunnerProfile?> Find(Guid id, CancellationToken ct); Task<IReadOnlyList<RunnerProfile>> Available(CancellationToken ct); Task Save(CancellationToken ct); }
public interface IRunnerFinanceRepository
{
    Task<RunnerPayoutAccount?> GetPayoutAccount(Guid runnerId, CancellationToken ct);
    void AddPayoutAccount(RunnerPayoutAccount account);
    Task<bool> HasEarning(Guid errandId, CancellationToken ct);
    void AddLedgerEntry(RunnerLedgerEntry entry);
    Task<decimal> Balance(Guid runnerId, string currency, CancellationToken ct);
    Task<IReadOnlyList<RunnerLedgerEntry>> ListLedger(Guid runnerId, int skip, int take, CancellationToken ct);
    Task<int> CountLedger(Guid runnerId, CancellationToken ct);
    Task<RunnerPayout?> FindPayout(Guid runnerId, string idempotencyKey, CancellationToken ct);
    Task<RunnerPayout?> FindPayoutByReference(string providerReference, CancellationToken ct);
    Task<bool> HasLedgerEntry(Guid payoutId, RunnerLedgerEntryType type, CancellationToken ct);
    void AddPayout(RunnerPayout payout);
    Task Save(CancellationToken ct);
}
public interface IPricingService { Money Estimate(int stopCount, decimal membershipDiscountPercent); }
public interface IRunnerMatchingService { Task<Guid?> FindRunner(Errand errand, CancellationToken ct); }
public interface IPaymentGateway { Task<PaymentIntent> CreateIntent(Guid paymentId, Money amount, CancellationToken ct); Task<bool> Verify(string providerReference, CancellationToken ct); }
public interface IPayoutGateway
{
    Task<PayoutRecipient> CreateRecipient(string bankCode, string accountNumber, string name, CancellationToken ct);
    Task<PayoutSubmission> Submit(Guid payoutId, Money amount, string recipientCode, CancellationToken ct);
}
public interface ICurrentUser { Guid UserId { get; } bool IsInRole(string role); }
public interface IClock { DateTimeOffset UtcNow { get; } }
public interface IPhoneOtpSender
{
    Task Send(string phoneNumber, string code, CancellationToken ct);
}

// Authentication contracts live in the application layer so the API does not
// depend directly on the chosen identity provider or persistence technology.
public sealed record RegisterAccount(string DisplayName, string Email, string Password, string? PhoneNumber = null);
public sealed record Login(string? EmailOrPhone, string Password, string? Email = null);
public sealed record ChangePassword(string CurrentPassword, string NewPassword);
public sealed record UpdateAccount(string DisplayName, string? PhoneNumber, string? Bio);
public sealed record ForgotPassword(string Email);
public sealed record ResetPassword(string Email, string Token, string NewPassword);
public sealed record PasswordResetTicket(string Token);
public sealed record PhoneVerificationChallengeDetails(
    Guid ChallengeId,
    string MaskedPhoneNumber,
    DateTimeOffset ExpiresAt,
    int RetryAfterSeconds,
    string? DevelopmentCode = null);
public sealed record VerifyPhoneNumber(Guid ChallengeId, string Code);
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
    Task<PhoneVerificationChallengeDetails> RequestPhoneVerification(Guid userId, bool includeDevelopmentCode, CancellationToken ct);
    Task<AuthenticationResult> VerifyPhoneNumber(Guid userId, VerifyPhoneNumber request, CancellationToken ct);
}
public sealed record PaymentIntent(string Reference, string CheckoutUrl);
public sealed record CreateStop(int Sequence, StopType Type, string Address, decimal Latitude, decimal Longitude, string? Instructions);
public sealed record CreateErrandItem(string Name, int Quantity, string? Unit = null, decimal? EstimatedUnitPrice = null);
public sealed record CreateErrand(
    string Title,
    ErrandCategory Category,
    DateTimeOffset? ScheduledFor,
    IReadOnlyList<CreateStop> Stops,
    decimal MerchandiseEstimate = 0,
    string Currency = "NGN",
    string? PreferredProvider = null,
    string? SpecialInstructions = null,
    IReadOnlyList<CreateErrandItem>? Items = null);
public sealed record MoneyDetails(decimal Amount, string Currency);
public sealed record ErrandSummary(Guid Id, string Title, ErrandCategory Category, ErrandStatus Status, int StopCount, Guid? RunnerId, MoneyDetails TotalEstimate, DateTimeOffset CreatedAt);
public sealed record ErrandStopDetails(Guid Id, int Sequence, StopType Type, string Address, decimal Latitude, decimal Longitude, string? Instructions, StopStatus Status, DateTimeOffset? CompletedAt);
public sealed record ErrandItemDetails(Guid Id, string Name, int Quantity, string Unit, decimal? EstimatedUnitPrice);
public sealed record ErrandDetails(Guid Id, string Title, ErrandCategory Category, ErrandStatus Status, DateTimeOffset? ScheduledFor, DateTimeOffset CreatedAt, Guid? RunnerId, string? PreferredProvider, string? SpecialInstructions, MoneyDetails MerchandiseEstimate, MoneyDetails ServiceFee, MoneyDetails TotalEstimate, IReadOnlyList<ErrandStopDetails> Stops, IReadOnlyList<ErrandItemDetails> Items);
public sealed record ErrandEstimate(Guid ErrandId, MoneyDetails Merchandise, MoneyDetails ServiceFee, MoneyDetails Total);
public sealed record ErrandTracking(Guid ErrandId, ErrandStatus Status, int CompletedStops, int TotalStops, ErrandStopDetails? CurrentStop, Guid? RunnerId);
public sealed record PagedErrands(IReadOnlyList<ErrandSummary> Items, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed record ErrandCategoryDetails(ErrandCategory Value, string Name, string Description, bool SupportsItems, bool SupportsPreferredProvider, bool SupportsPrescription);
public sealed record SetRunnerAvailability(bool Available);
public sealed record RunnerDashboard(RunnerStatus Status, bool IsAvailable, decimal Rating, int CompletedErrands, MoneyDetails AvailableBalance, int ActiveJobs);
public sealed record RunnerJobSummary(Guid Id, string Title, ErrandCategory Category, ErrandStatus Status, int StopCount, MoneyDetails ExpectedEarnings, DateTimeOffset? ScheduledFor);
public sealed record PagedRunnerJobs(IReadOnlyList<RunnerJobSummary> Items, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed record RunnerJobDetails(ErrandDetails Errand, MoneyDetails ExpectedEarnings);
public sealed record PayoutRecipient(string RecipientCode, string AccountName, string AccountNumberLast4);
public sealed record PayoutSubmission(string Reference);
public sealed record SetPayoutAccount(string BankCode, string BankName, string AccountNumber, bool InstantPayout);
public sealed record PayoutAccountDetails(string BankCode, string BankName, string AccountName, string MaskedAccountNumber, bool InstantPayout, DateTimeOffset UpdatedAt);
public sealed record RequestPayout(decimal Amount, string Currency = "NGN");
public sealed record PayoutDetails(Guid Id, MoneyDetails Amount, MoneyDetails Fee, RunnerPayoutStatus Status, string? ProviderReference, string? FailureReason, DateTimeOffset RequestedAt, DateTimeOffset? ProcessedAt);
public sealed record LedgerEntryDetails(Guid Id, RunnerLedgerEntryType Type, MoneyDetails Amount, string Description, Guid? ErrandId, Guid? PayoutId, DateTimeOffset CreatedAt);
public sealed record EarningsDashboard(MoneyDetails AvailableBalance, IReadOnlyList<LedgerEntryDetails> Transactions, int Page, int PageSize, int TotalCount);
public sealed class PricingService : IPricingService
{
    private const decimal BaseFee = 2500m, AdditionalStopFee = 750m;
    public Money Estimate(int stopCount, decimal discount) { if (stopCount < 2) throw new DomainException("At least two stops are required."); var subtotal = BaseFee + (stopCount - 2) * AdditionalStopFee; return new(subtotal * (1 - Math.Clamp(discount, 0, 100) / 100)); }
}
public sealed class RunnerMatchingService(IRunnerRepository runners) : IRunnerMatchingService
{
    public async Task<Guid?> FindRunner(Errand errand, CancellationToken ct) => (await runners.Available(ct)).OrderByDescending(x => x.Rating).ThenByDescending(x => x.CompletedErrands).Select(x => (Guid?)x.UserId).FirstOrDefault();
}
public sealed class RunnerCompensationPolicy(decimal runnerPercent, decimal payoutFee = 50m)
{
    public decimal RunnerPercent { get; } = runnerPercent is > 0 and <= 100 ? runnerPercent : throw new ArgumentOutOfRangeException(nameof(runnerPercent));
    public decimal PayoutFee { get; } = payoutFee >= 0 ? payoutFee : throw new ArgumentOutOfRangeException(nameof(payoutFee));
    public Money Calculate(Errand errand) => new(errand.ServiceFee * RunnerPercent / 100m, errand.Currency);
}
public sealed class ErrandService(IErrandRepository errands, IRunnerRepository runners, IRunnerFinanceRepository finance, IRunnerMatchingService matching, IPricingService pricing, RunnerCompensationPolicy compensation, INotificationPublisher notifications, ICurrentUser current, IClock clock)
{
    public async Task<ErrandSummary> Create(CreateErrand command, CancellationToken ct)
    {
        if (!current.IsInRole("Customer")) throw new UnauthorizedAccessException();
        if (command.Stops is null) throw new DomainException("Stops are required.");
        if (command.ScheduledFor < clock.UtcNow) throw new DomainException("Scheduled time cannot be in the past.");
        var merchandise = new Money(command.MerchandiseEstimate, command.Currency);
        var errand = new Errand(Guid.NewGuid(), current.UserId, command.Title, command.Category, command.ScheduledFor, command.PreferredProvider, command.SpecialInstructions);
        foreach (var s in command.Stops.OrderBy(x => x.Sequence)) errand.AddStop(new(Guid.NewGuid(), s.Sequence, s.Type, s.Address, new(s.Latitude, s.Longitude), s.Instructions));
        foreach (var item in command.Items ?? []) errand.AddItem(new(Guid.NewGuid(), item.Name, item.Quantity, item.Unit, item.EstimatedUnitPrice));
        errand.RequestEstimate();
        errand.SetEstimate(pricing.Estimate(errand.Stops.Count, 0), merchandise);
        await errands.Add(errand, ct); await errands.Save(ct); return Map(errand);
    }
    public async Task<PagedErrands> List(bool? active, int page, int pageSize, CancellationToken ct)
    {
        EnsureCustomer();
        page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await errands.CountForUser(current.UserId, false, active, ct);
        var values = await errands.ListForUser(current.UserId, false, active, (page - 1) * pageSize, pageSize, ct);
        return new(values.Select(Map).ToList(), page, pageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    }
    public async Task<ErrandDetails> Get(Guid id, CancellationToken ct) => MapDetails(await Owned(id, false, ct));
    public async Task<ErrandEstimate> GetEstimate(Guid id, CancellationToken ct)
    {
        var e = await Owned(id, false, ct);
        return new(e.Id, Money(e.MerchandiseEstimate, e.Currency), Money(e.ServiceFee, e.Currency), Money(e.TotalEstimate, e.Currency));
    }
    public async Task<ErrandSummary> Cancel(Guid id, CancellationToken ct)
    {
        var e = await Owned(id, false, ct);
        var runnerId = e.RunnerId;
        e.Cancel(current.UserId);
        if (runnerId is Guid idOfRunner)
        {
            var runner = await runners.Find(idOfRunner, ct);
            if (runner?.Status == RunnerStatus.Busy) runner.ReleaseAssignment();
        }
        await errands.Save(ct);
        if(runnerId is Guid cancelledRunner)await notifications.Publish(cancelledRunner,NotificationType.ErrandUpdate,"Errand cancelled",e.Title,e.Id,ct);
        return Map(e);
    }
    public async Task<ErrandSummary> ConfirmCompletion(Guid id, CancellationToken ct)
    {
        var e = await Owned(id, false, ct); e.ConfirmCompletion(current.UserId);
        if (e.RunnerId is Guid runnerId)
        {
            var runner = await runners.Find(runnerId, ct) ?? throw new KeyNotFoundException("Runner not found.");
            runner.CompleteErrand();
            if (!await finance.HasEarning(e.Id, ct))
                finance.AddLedgerEntry(new RunnerLedgerEntry(Guid.NewGuid(), runnerId, e.Id, null, RunnerLedgerEntryType.Earning, compensation.Calculate(e), $"Completed errand - {e.Title}", clock.UtcNow));
        }
        await errands.Save(ct);
        if(e.RunnerId is Guid paidRunner)await notifications.Publish(paidRunner,NotificationType.Payment,"Earnings available",$"Your earnings for {e.Title} are now available.",e.Id,ct);
        return Map(e);
    }
    public async Task<ErrandTracking> Track(Guid id, CancellationToken ct)
    {
        var e = await Owned(id, false, ct);
        var completed = e.Stops.Count(x => x.Status == StopStatus.Completed);
        var current = e.Stops.FirstOrDefault(x => x.Status is StopStatus.Active or StopStatus.Pending);
        return new(e.Id, e.Status, completed, e.Stops.Count, current is null ? null : MapStop(current), e.RunnerId);
    }
    public async Task<ErrandSummary> Match(Guid id, CancellationToken ct)
    {
        var e = await Owned(id, false, ct); e.BeginMatching(); var runnerId = await matching.FindRunner(e, ct) ?? throw new DomainException("No eligible runner is currently available.");
        var runner = await runners.Find(runnerId, ct) ?? throw new DomainException("Matched runner was not found.");
        runner.Assign(); e.AssignRunner(runnerId); await errands.Save(ct);await notifications.Publish(runnerId,NotificationType.NewAssignment,"New errand assignment",e.Title,e.Id,ct);return Map(e);
    }
    public async Task<ErrandSummary> Accept(Guid id, CancellationToken ct) { var e = await Owned(id, true, ct); e.Accept(current.UserId); await errands.Save(ct);await notifications.Publish(e.CustomerId,NotificationType.ErrandUpdate,"Runner accepted your errand",e.Title,e.Id,ct);return Map(e); }
    public async Task<ErrandSummary> Decline(Guid id, CancellationToken ct) { var e = await Owned(id, true, ct); e.Decline(current.UserId); var runner = await runners.Find(current.UserId, ct) ?? throw new KeyNotFoundException("Runner not found."); runner.ReleaseAssignment(); await errands.Save(ct); return Map(e); }
    public async Task<ErrandSummary> StartJourney(Guid id, CancellationToken ct) { var e = await Owned(id, true, ct); e.StartJourney(current.UserId); await errands.Save(ct);await notifications.Publish(e.CustomerId,NotificationType.ErrandUpdate,"Runner is on the way",e.Title,e.Id,ct);return Map(e); }
    public async Task<ErrandSummary> StartStop(Guid id, Guid stopId, CancellationToken ct) { var e = await Owned(id, true, ct); e.StartStop(current.UserId, stopId); await errands.Save(ct); return Map(e); }
    public async Task<ErrandSummary> CompleteStop(Guid id, Guid stopId, CancellationToken ct) { var e = await Owned(id, true, ct); e.CompleteStop(current.UserId, stopId, clock.UtcNow); await errands.Save(ct);await notifications.Publish(e.CustomerId,NotificationType.ErrandUpdate,"Errand stop completed",$"Progress: {e.Stops.Count(x=>x.Status==StopStatus.Completed)} of {e.Stops.Count} stops.",e.Id,ct);return Map(e); }
    private async Task<Errand> Owned(Guid id, bool runner, CancellationToken ct) { var e = await errands.Find(id, ct) ?? throw new KeyNotFoundException("Errand not found."); var owner = runner ? e.RunnerId : e.CustomerId; if (owner != current.UserId) throw new UnauthorizedAccessException(); return e; }
    private void EnsureCustomer() { if (!current.IsInRole("Customer")) throw new UnauthorizedAccessException(); }
    private static MoneyDetails Money(decimal amount, string currency) => new(amount, currency);
    private static ErrandSummary Map(Errand e) => new(e.Id, e.Title, e.Category, e.Status, e.Stops.Count, e.RunnerId, Money(e.TotalEstimate, e.Currency), e.CreatedAt);
    private static ErrandStopDetails MapStop(ErrandStop s) => new(s.Id, s.Sequence, s.Type, s.Address, s.Location.Latitude, s.Location.Longitude, s.Instructions, s.Status, s.CompletedAt);
    private static ErrandDetails MapDetails(Errand e) => new(e.Id, e.Title, e.Category, e.Status, e.ScheduledFor, e.CreatedAt, e.RunnerId, e.PreferredProvider, e.SpecialInstructions, Money(e.MerchandiseEstimate, e.Currency), Money(e.ServiceFee, e.Currency), Money(e.TotalEstimate, e.Currency), e.Stops.Select(MapStop).ToList(), e.Items.Select(i => new ErrandItemDetails(i.Id, i.Name, i.Quantity, i.Unit, i.EstimatedUnitPrice)).ToList());
    public static ErrandDetails ToDetails(Errand e) => MapDetails(e);
}
