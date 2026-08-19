using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Payments;
using ErrandRuns.Domain.Runners;

namespace ErrandRuns.Application;

public sealed class RunnerService(
    IRunnerRepository runners,
    IErrandRepository errands,
    IRunnerFinanceRepository finance,
    RunnerCompensationPolicy compensation,
    ICurrentUser current)
{
    public async Task<RunnerDashboard> Dashboard(CancellationToken ct)
    {
        var runner = await Profile(ct);
        var jobs = await errands.CountForUser(current.UserId, true, true, ct);
        var balance = await finance.Balance(current.UserId, "NGN", ct);
        return new(runner.Status, runner.Status == RunnerStatus.Available, runner.Rating,
            runner.CompletedErrands, new(balance, "NGN"), jobs);
    }

    public async Task<RunnerDashboard> SetAvailability(bool available, CancellationToken ct)
    {
        var runner = await Profile(ct);
        runner.SetAvailable(available);
        await runners.Save(ct);
        return await Dashboard(ct);
    }

    public async Task<RunnerDashboard> SubmitVerification(CancellationToken ct)
    {
        var runner = await Profile(ct);
        runner.SubmitVerification();
        await runners.Save(ct);
        return await Dashboard(ct);
    }

    public async Task<PagedRunnerJobs> Jobs(bool? active, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await errands.CountForUser(current.UserId, true, active, ct);
        var values = await errands.ListForUser(current.UserId, true, active, (page - 1) * pageSize, pageSize, ct);
        var summaries = values.Select(e =>
        {
            var earning = compensation.Calculate(e);
            return new RunnerJobSummary(e.Id, e.Title, e.Category, e.Status,
                e.Stops.Count, new(earning.Amount, earning.Currency), e.ScheduledFor);
        }).ToList();
        return new(summaries, page, pageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<RunnerJobDetails> Job(Guid id, CancellationToken ct)
    {
        var errand = await errands.Find(id, ct) ?? throw new KeyNotFoundException("Errand not found.");
        if (errand.RunnerId != current.UserId) throw new UnauthorizedAccessException();
        var earning = compensation.Calculate(errand);
        return new(ErrandService.ToDetails(errand), new(earning.Amount, earning.Currency));
    }

    private async Task<RunnerProfile> Profile(CancellationToken ct) =>
        await runners.Find(current.UserId, ct) ?? throw new KeyNotFoundException("Runner profile not found.");
}

public sealed class RunnerFinanceService(
    IRunnerFinanceRepository finance,
    IRunnerRepository runners,
    IPayoutGateway gateway,
    RunnerCompensationPolicy compensation,
    ICurrentUser current,
    IClock clock)
{
    public async Task<PayoutAccountDetails?> GetAccount(CancellationToken ct) =>
        Map(await finance.GetPayoutAccount(current.UserId, ct));

    public async Task<PayoutAccountDetails> SetAccount(SetPayoutAccount request, CancellationToken ct)
    {
        await EnsureVerified(ct);
        if (request.AccountNumber is null || request.AccountNumber.Length is < 10 or > 20 ||
            !request.AccountNumber.All(char.IsDigit))
            throw new DomainException("A valid account number is required.");

        var recipient = await gateway.CreateRecipient(
            request.BankCode, request.AccountNumber, request.BankName, ct);
        var account = await finance.GetPayoutAccount(current.UserId, ct);
        if (account is null)
        {
            account = new(current.UserId, request.BankCode, request.BankName,
                recipient.AccountName, recipient.AccountNumberLast4,
                recipient.RecipientCode, request.InstantPayout);
            finance.AddPayoutAccount(account);
        }
        else
        {
            account.Update(request.BankCode, request.BankName, recipient.AccountName,
                recipient.AccountNumberLast4, recipient.RecipientCode, request.InstantPayout);
        }
        await finance.Save(ct);
        return Map(account)!;
    }

    public async Task<EarningsDashboard> Earnings(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await finance.CountLedger(current.UserId, ct);
        var entries = await finance.ListLedger(current.UserId, (page - 1) * pageSize, pageSize, ct);
        var balance = await finance.Balance(current.UserId, "NGN", ct);
        return new(new(balance, "NGN"), entries.Select(e => new LedgerEntryDetails(
            e.Id, e.Type, new(e.Amount, e.Currency), e.Description, e.ErrandId,
            e.PayoutId, e.CreatedAt)).ToList(), page, pageSize, total);
    }

    public async Task<PayoutDetails> RequestPayout(
        RequestPayout request, string idempotencyKey, CancellationToken ct)
    {
        await EnsureVerified(ct);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new DomainException("Idempotency-Key header is required.");
        var existing = await finance.FindPayout(current.UserId, idempotencyKey, ct);
        if (existing is not null) return Map(existing);

        var amount = new Money(request.Amount, request.Currency);
        var fee = new Money(compensation.PayoutFee, request.Currency);
        if (amount.Amount <= 0) throw new DomainException("Payout amount must be positive.");
        var account = await finance.GetPayoutAccount(current.UserId, ct)
            ?? throw new DomainException("Configure a payout account first.");
        if (await finance.Balance(current.UserId, amount.Currency, ct) < amount.Amount + fee.Amount)
            throw new DomainException("Insufficient available balance.");

        var payout = new RunnerPayout(Guid.NewGuid(), current.UserId, amount, fee,
            idempotencyKey, clock.UtcNow);
        finance.AddPayout(payout);
        await finance.Save(ct);
        try
        {
            var submitted = await gateway.Submit(payout.Id, amount, account.RecipientCode, ct);
            payout.Submit(submitted.Reference, clock.UtcNow);
            finance.AddLedgerEntry(new RunnerLedgerEntry(Guid.NewGuid(), current.UserId,
                null, payout.Id, RunnerLedgerEntryType.Payout, new Money(amount.Amount + fee.Amount, amount.Currency),
                "Withdrawal to " + account.BankName, clock.UtcNow));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            payout.Fail("Payout provider rejected the transfer.", clock.UtcNow);
            await finance.Save(ct);
            throw new DomainException("Payout could not be submitted.");
        }
        await finance.Save(ct);
        return Map(payout);
    }

    public async Task ReconcilePayout(string reference, string outcome, CancellationToken ct)
    {
        var payout = await finance.FindPayoutByReference(reference, ct);
        if (payout is null) return;
        if (outcome == "success") payout.MarkPaid(clock.UtcNow);
        else if (outcome is "failed" or "reversed")
        {
            if (outcome == "failed") payout.MarkTransferFailed("Paystack reported a failed transfer.", clock.UtcNow);
            else payout.Reverse("Paystack reversed the transfer.", clock.UtcNow);
            if (!await finance.HasLedgerEntry(payout.Id, RunnerLedgerEntryType.PayoutReversal, ct))
                finance.AddLedgerEntry(new RunnerLedgerEntry(Guid.NewGuid(), payout.RunnerId, null,
                    payout.Id, RunnerLedgerEntryType.PayoutReversal,
                    new Money(payout.Amount + payout.Fee, payout.Currency), "Reversed payout", clock.UtcNow));
        }
        await finance.Save(ct);
    }

    private static PayoutAccountDetails? Map(RunnerPayoutAccount? account) =>
        account is null ? null : new(account.BankCode, account.BankName,
            account.AccountName, "******" + account.AccountNumberLast4,
            account.InstantPayout, account.UpdatedAt);

    private static PayoutDetails Map(RunnerPayout payout) => new(payout.Id,
        new(payout.Amount, payout.Currency), new(payout.Fee, payout.Currency), payout.Status,
        string.IsNullOrEmpty(payout.ProviderReference) ? null : payout.ProviderReference,
        string.IsNullOrEmpty(payout.FailureReason) ? null : payout.FailureReason,
        payout.RequestedAt, payout.ProcessedAt);

    private async Task EnsureVerified(CancellationToken ct)
    {
        var runner = await runners.Find(current.UserId, ct)
            ?? throw new KeyNotFoundException("Runner profile not found.");
        if (runner.Status is RunnerStatus.Applicant or RunnerStatus.PendingVerification or
            RunnerStatus.Suspended or RunnerStatus.Rejected or RunnerStatus.Deactivated)
            throw new DomainException("Runner verification is required for payouts.");
    }
}
