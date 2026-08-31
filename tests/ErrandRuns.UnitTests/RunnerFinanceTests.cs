using ErrandRuns.Application;
using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Errands;
using ErrandRuns.Domain.Payments;
using ErrandRuns.Domain.Runners;

namespace ErrandRuns.UnitTests;

public sealed class RunnerFinanceTests
{
    [Fact]
    public void Busy_runner_returns_available_after_completion()
    {
        var runner = new RunnerProfile(Guid.NewGuid());
        runner.SubmitVerification(); runner.Approve(); runner.SetAvailable(true); runner.Assign();
        runner.CompleteErrand();
        Assert.Equal(RunnerStatus.Available, runner.Status);
        Assert.Equal(1, runner.CompletedErrands);
    }

    [Fact]
    public void Compensation_uses_service_fee_not_merchandise_budget()
    {
        var errand = new Errand(Guid.NewGuid(), Guid.NewGuid(), "Grocery", ErrandCategory.Grocery);
        errand.AddStop(new(Guid.NewGuid(), 1, StopType.Shopping, "Store", new(6.5m, 3.3m), null));
        errand.AddStop(new(Guid.NewGuid(), 2, StopType.Delivery, "Home", new(6.6m, 3.4m), null));
        errand.RequestEstimate(); errand.SetEstimate(new Money(2500), new Money(50000));
        var earning = new RunnerCompensationPolicy(80).Calculate(errand);
        Assert.Equal(2000, earning.Amount);
    }

    [Fact]
    public void Payout_requires_a_positive_amount()
    {
        Assert.Throws<DomainException>(() => new RunnerPayout(
            Guid.NewGuid(), Guid.NewGuid(), new Money(0), new Money(50), "key", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Customer_confirmation_credits_runner_and_payout_debits_balance()
    {
        var ct=TestContext.Current.CancellationToken;
        var customerId=Guid.NewGuid();var runnerId=Guid.NewGuid();var runner=new RunnerProfile(runnerId);
        runner.SubmitVerification();runner.Approve();runner.SetAvailable(true);runner.Assign();
        var errand=new Errand(Guid.NewGuid(),customerId,"Paid grocery run",ErrandCategory.Grocery);
        errand.AddStop(new(Guid.NewGuid(),1,StopType.Shopping,"Store",new(6.5m,3.3m),null));errand.AddStop(new(Guid.NewGuid(),2,StopType.Delivery,"Home",new(6.6m,3.4m),null));
        errand.RequestEstimate();errand.SetEstimate(new Money(2500),new Money(10000));errand.ConfirmPayment();errand.BeginMatching();errand.AssignRunner(runnerId);errand.Accept(runnerId);errand.StartJourney(runnerId);
        foreach(var stop in errand.Stops){errand.StartStop(runnerId,stop.Id);errand.CompleteStop(runnerId,stop.Id,DateTimeOffset.UtcNow);}
        var errandRepo=new FakeErrands(errand);var runnerRepo=new FakeRunners(runner);var finance=new FakeFinance();var policy=new RunnerCompensationPolicy(80,50);var clock=new FakeClock();
        var customer=new FakeCurrent(customerId,"Customer");var service=new ErrandService(errandRepo,runnerRepo,finance,new RunnerMatchingService(runnerRepo),new PricingService(),policy,new FakeNotifications(),customer,clock);
        await service.ConfirmCompletion(errand.Id,ct);
        Assert.Equal(2000,await finance.Balance(runnerId,"NGN",ct));
        finance.AddPayoutAccount(new RunnerPayoutAccount(runnerId,"058","GTBank","Test Runner","5678","recipient",false));
        var payouts=new RunnerFinanceService(finance,runnerRepo,new FakeGateway(),policy,new FakeCurrent(runnerId,"Runner"),clock);
        var result=await payouts.RequestPayout(new RequestPayout(1000),"payout-1",ct);
        Assert.Equal(50,result.Fee.Amount);Assert.Equal(950,await finance.Balance(runnerId,"NGN",ct));
        await payouts.ReconcilePayout("transfer","failed",ct);
        Assert.Equal(2000,await finance.Balance(runnerId,"NGN",ct));
    }

    private sealed class FakeCurrent(Guid id,string role):ICurrentUser{public Guid UserId=>id;public bool IsInRole(string value)=>value==role;}
    private sealed class FakeClock:IClock{public DateTimeOffset UtcNow=>DateTimeOffset.UtcNow;}
    private sealed class FakeGateway:IPayoutGateway{public Task<PayoutRecipient>CreateRecipient(string b,string a,string n,CancellationToken ct)=>Task.FromResult(new PayoutRecipient("recipient",n,a[^4..]));public Task<PayoutSubmission>Submit(Guid id,Money amount,string code,CancellationToken ct)=>Task.FromResult(new PayoutSubmission("transfer"));}
    private sealed class FakeNotifications:INotificationPublisher{public Task Publish(Guid id,ErrandRuns.Domain.Communications.NotificationType type,string title,string body,Guid? errandId,CancellationToken ct)=>Task.CompletedTask;}
    private sealed class FakeErrands(Errand errand):IErrandRepository
    {public Task Add(Errand value,CancellationToken ct)=>Task.CompletedTask;public Task<Errand?>Find(Guid id,CancellationToken ct)=>Task.FromResult<Errand?>(id==errand.Id?errand:null);public Task<IReadOnlyList<Errand>>ListForUser(Guid id,bool runner,bool? active,int skip,int take,CancellationToken ct)=>Task.FromResult<IReadOnlyList<Errand>>([errand]);public Task<int>CountForUser(Guid id,bool runner,bool? active,CancellationToken ct)=>Task.FromResult(1);public Task<IReadOnlyList<Errand>>SearchForCustomer(Guid id,string query,int skip,int take,CancellationToken ct)=>Task.FromResult<IReadOnlyList<Errand>>([errand]);public Task<int>CountSearchForCustomer(Guid id,string query,CancellationToken ct)=>Task.FromResult(1);public Task Save(CancellationToken ct)=>Task.CompletedTask;}
    private sealed class FakeRunners(RunnerProfile runner):IRunnerRepository
    {public Task<RunnerProfile?>Find(Guid id,CancellationToken ct)=>Task.FromResult<RunnerProfile?>(id==runner.UserId?runner:null);public Task<IReadOnlyList<RunnerProfile>>Available(CancellationToken ct)=>Task.FromResult<IReadOnlyList<RunnerProfile>>([runner]);public Task Save(CancellationToken ct)=>Task.CompletedTask;}
    private sealed class FakeFinance:IRunnerFinanceRepository
    {
        private readonly List<RunnerLedgerEntry> ledger=[];private readonly List<RunnerPayout> payouts=[];private RunnerPayoutAccount? account;
        public Task<RunnerPayoutAccount?>GetPayoutAccount(Guid id,CancellationToken ct)=>Task.FromResult(account);public void AddPayoutAccount(RunnerPayoutAccount value)=>account=value;public Task<bool>HasEarning(Guid id,CancellationToken ct)=>Task.FromResult(ledger.Any(x=>x.ErrandId==id));public void AddLedgerEntry(RunnerLedgerEntry value)=>ledger.Add(value);public Task<decimal>Balance(Guid id,string currency,CancellationToken ct)=>Task.FromResult(ledger.Where(x=>x.RunnerId==id&&x.Currency==currency).Sum(x=>x.Type is RunnerLedgerEntryType.Earning or RunnerLedgerEntryType.PayoutReversal?x.Amount:-x.Amount));public Task<IReadOnlyList<RunnerLedgerEntry>>ListLedger(Guid id,int skip,int take,CancellationToken ct)=>Task.FromResult<IReadOnlyList<RunnerLedgerEntry>>(ledger.Skip(skip).Take(take).ToList());public Task<int>CountLedger(Guid id,CancellationToken ct)=>Task.FromResult(ledger.Count);public Task<RunnerPayout?>FindPayout(Guid id,string key,CancellationToken ct)=>Task.FromResult(payouts.SingleOrDefault(x=>x.RunnerId==id&&x.IdempotencyKey==key));public Task<RunnerPayout?>FindPayoutByReference(string value,CancellationToken ct)=>Task.FromResult(payouts.SingleOrDefault(x=>x.ProviderReference==value));public Task<bool>HasLedgerEntry(Guid id,RunnerLedgerEntryType type,CancellationToken ct)=>Task.FromResult(ledger.Any(x=>x.PayoutId==id&&x.Type==type));public void AddPayout(RunnerPayout value)=>payouts.Add(value);public Task Save(CancellationToken ct)=>Task.CompletedTask;
    }
}
