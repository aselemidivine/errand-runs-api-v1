using ErrandRuns.Domain.Common;

namespace ErrandRuns.Domain.Payments;

public enum RunnerLedgerEntryType { Earning, Payout, PayoutReversal }
public enum RunnerPayoutStatus { Pending, Submitted, Paid, Failed, Reversed }

public sealed class RunnerLedgerEntry
{
    private RunnerLedgerEntry() { Currency = "NGN"; Description = string.Empty; }
    public RunnerLedgerEntry(Guid id, Guid runnerId, Guid? errandId, Guid? payoutId, RunnerLedgerEntryType type, Money amount, string description, DateTimeOffset createdAt)
    {
        if (amount.Amount <= 0) throw new DomainException("Ledger amount must be positive.");
        Id = id; RunnerId = runnerId; ErrandId = errandId; PayoutId = payoutId; Type = type;
        Amount = amount.Amount; Currency = amount.Currency; Description = description; CreatedAt = createdAt;
    }
    public Guid Id { get; private set; }
    public Guid RunnerId { get; private set; }
    public Guid? ErrandId { get; private set; }
    public Guid? PayoutId { get; private set; }
    public RunnerLedgerEntryType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class RunnerPayoutAccount
{
    private RunnerPayoutAccount() { BankCode = BankName = AccountName = AccountNumberLast4 = RecipientCode = string.Empty; }
    public RunnerPayoutAccount(Guid runnerId, string bankCode, string bankName, string accountName, string accountNumberLast4, string recipientCode, bool instantPayout)
    { RunnerId = runnerId; Update(bankCode, bankName, accountName, accountNumberLast4, recipientCode, instantPayout); }
    public Guid RunnerId { get; private set; }
    public string BankCode { get; private set; } = string.Empty;
    public string BankName { get; private set; } = string.Empty;
    public string AccountName { get; private set; } = string.Empty;
    public string AccountNumberLast4 { get; private set; } = string.Empty;
    public string RecipientCode { get; private set; } = string.Empty;
    public bool InstantPayout { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public void Update(string bankCode, string bankName, string accountName, string last4, string recipientCode, bool instantPayout)
    {
        if (string.IsNullOrWhiteSpace(bankCode) || string.IsNullOrWhiteSpace(bankName) || string.IsNullOrWhiteSpace(accountName) || last4.Length != 4 || string.IsNullOrWhiteSpace(recipientCode))
            throw new DomainException("Complete verified bank details are required.");
        BankCode = bankCode; BankName = bankName; AccountName = accountName; AccountNumberLast4 = last4; RecipientCode = recipientCode; InstantPayout = instantPayout; UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class RunnerPayout
{
    private RunnerPayout() { Currency = "NGN"; IdempotencyKey = ProviderReference = FailureReason = string.Empty; }
    public RunnerPayout(Guid id, Guid runnerId, Money amount, Money fee, string idempotencyKey, DateTimeOffset requestedAt)
    {
        if (amount.Amount <= 0) throw new DomainException("Payout amount must be positive.");
        if (amount.Currency != fee.Currency) throw new DomainException("Payout fee currency mismatch.");
        Id = id; RunnerId = runnerId; Amount = amount.Amount; Fee = fee.Amount; Currency = amount.Currency; IdempotencyKey = idempotencyKey; RequestedAt = requestedAt;
    }
    public Guid Id { get; private set; }
    public Guid RunnerId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal Fee { get; private set; }
    public string Currency { get; private set; }
    public RunnerPayoutStatus Status { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string ProviderReference { get; private set; } = string.Empty;
    public string FailureReason { get; private set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public void Submit(string reference, DateTimeOffset now) { if (Status != RunnerPayoutStatus.Pending) throw new DomainException("Payout is not pending."); ProviderReference = reference; Status = RunnerPayoutStatus.Submitted; ProcessedAt = now; }
    public void Fail(string reason, DateTimeOffset now) { if (Status != RunnerPayoutStatus.Pending) throw new DomainException("Payout is not pending."); FailureReason = reason; Status = RunnerPayoutStatus.Failed; ProcessedAt = now; }
    public void MarkPaid(DateTimeOffset now) { if (Status == RunnerPayoutStatus.Paid) return; if (Status != RunnerPayoutStatus.Submitted) throw new DomainException("Only a submitted payout can complete."); Status = RunnerPayoutStatus.Paid; ProcessedAt = now; }
    public void MarkTransferFailed(string reason, DateTimeOffset now) { if (Status == RunnerPayoutStatus.Failed) return; if (Status != RunnerPayoutStatus.Submitted) throw new DomainException("Only a submitted payout can fail."); FailureReason = reason; Status = RunnerPayoutStatus.Failed; ProcessedAt = now; }
    public void Reverse(string reason, DateTimeOffset now) { if (Status == RunnerPayoutStatus.Reversed) return; if (Status is not (RunnerPayoutStatus.Submitted or RunnerPayoutStatus.Paid)) throw new DomainException("Payout cannot be reversed."); FailureReason = reason; Status = RunnerPayoutStatus.Reversed; ProcessedAt = now; }
}
