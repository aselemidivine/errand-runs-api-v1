using ErrandRuns.Application;
using ErrandRuns.Domain.Errands;
using ErrandRuns.Domain.Communications;
using ErrandRuns.Domain.Payments;
using ErrandRuns.Domain.Runners;
using ErrandRuns.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace ErrandRuns.Infrastructure;

public sealed class ErrandRunsDbContext(DbContextOptions<ErrandRunsDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Errand> Errands => Set<Errand>(); public DbSet<RunnerProfile> Runners => Set<RunnerProfile>(); public DbSet<Payment> Payments => Set<Payment>(); public DbSet<RunnerLedgerEntry> RunnerLedger => Set<RunnerLedgerEntry>(); public DbSet<RunnerPayoutAccount> RunnerPayoutAccounts => Set<RunnerPayoutAccount>(); public DbSet<RunnerPayout> RunnerPayouts => Set<RunnerPayout>(); public DbSet<UserNotification> Notifications => Set<UserNotification>(); public DbSet<Conversation> Conversations => Set<Conversation>(); public DbSet<VoiceCallSession> VoiceCalls => Set<VoiceCallSession>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema("app");
        // Keep identity data separate from operational errand data. This also
        // makes retention and access policies easier to apply independently.
        b.Entity<ApplicationUser>(e => { e.ToTable("Users", "identity"); e.Property(x => x.DisplayName).HasMaxLength(120); e.Property(x => x.PhoneNumber).HasMaxLength(32); e.Property(x => x.Bio).HasMaxLength(300); e.HasIndex(x => x.PhoneNumber).IsUnique().HasFilter("[PhoneNumber] IS NOT NULL"); });
        b.Entity<IdentityRole<Guid>>().ToTable("Roles", "identity");
        b.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles", "identity");
        b.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims", "identity");
        b.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins", "identity");
        b.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims", "identity");
        b.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens", "identity");
        b.Entity<Errand>(e => { e.ToTable("Errands"); e.HasKey(x => x.Id); e.Property(x => x.Title).HasMaxLength(160); e.Property(x => x.PreferredProvider).HasMaxLength(160); e.Property(x => x.SpecialInstructions).HasMaxLength(1000); e.Property(x => x.MerchandiseEstimate).HasPrecision(18, 2); e.Property(x => x.ServiceFee).HasPrecision(18, 2); e.Property(x => x.Currency).HasMaxLength(3); e.Ignore(x => x.TotalEstimate); e.Property(x => x.RowVersion).IsRowVersion(); e.HasIndex(x => new { x.CustomerId, x.CreatedAt }); e.OwnsMany(x => x.Stops, s => { s.ToTable("ErrandStops"); s.WithOwner().HasForeignKey("ErrandId"); s.HasKey(x => x.Id); s.HasIndex("ErrandId", nameof(ErrandStop.Sequence)).IsUnique(); s.Property(x => x.Address).HasMaxLength(500); s.OwnsOne(x => x.Location, g => { g.Property(x => x.Latitude).HasPrecision(9, 6); g.Property(x => x.Longitude).HasPrecision(9, 6); }); }); e.OwnsMany(x => x.Items, i => { i.ToTable("ErrandItems"); i.WithOwner().HasForeignKey("ErrandId"); i.HasKey(x => x.Id); i.Property(x => x.Name).HasMaxLength(160); i.Property(x => x.Unit).HasMaxLength(40); i.Property(x => x.EstimatedUnitPrice).HasPrecision(18, 2); }); });
        b.Entity<RunnerProfile>(e => { e.ToTable("RunnerProfiles", "runners"); e.HasKey(x => x.UserId); e.Property(x => x.Rating).HasPrecision(3, 2); e.HasIndex(x => x.Status); e.HasOne<ApplicationUser>().WithOne().HasForeignKey<RunnerProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<Payment>(e => { e.ToTable("Payments", "payments"); e.HasKey(x => x.Id); e.Property(x => x.RowVersion).IsRowVersion(); e.HasIndex(x => x.IdempotencyKey).IsUnique(); e.HasIndex(x => x.ProviderReference).IsUnique().HasFilter("[ProviderReference] <> ''"); e.ComplexProperty(x => x.Amount, m => { m.Property(x => x.Amount).HasColumnName("Amount").HasPrecision(18, 2); m.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3); }); });
        b.Entity<RunnerLedgerEntry>(e => { e.ToTable("RunnerLedger", "payments"); e.HasKey(x => x.Id); e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.Currency).HasMaxLength(3); e.Property(x => x.Description).HasMaxLength(240); e.HasIndex(x => x.RunnerId); e.HasIndex(x => x.ErrandId).IsUnique().HasFilter("[ErrandId] IS NOT NULL"); e.HasIndex(x => new { x.PayoutId, x.Type }).IsUnique().HasFilter("[PayoutId] IS NOT NULL"); });
        b.Entity<RunnerPayoutAccount>(e => { e.ToTable("RunnerPayoutAccounts", "payments"); e.HasKey(x => x.RunnerId); e.Property(x => x.BankCode).HasMaxLength(20); e.Property(x => x.BankName).HasMaxLength(120); e.Property(x => x.AccountName).HasMaxLength(160); e.Property(x => x.AccountNumberLast4).HasMaxLength(4); e.Property(x => x.RecipientCode).HasMaxLength(120); });
        b.Entity<RunnerPayout>(e => { e.ToTable("RunnerPayouts", "payments"); e.HasKey(x => x.Id); e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.Fee).HasPrecision(18, 2); e.Property(x => x.Currency).HasMaxLength(3); e.Property(x => x.IdempotencyKey).HasMaxLength(120); e.Property(x => x.ProviderReference).HasMaxLength(160); e.Property(x => x.FailureReason).HasMaxLength(500); e.Property(x => x.RowVersion).IsRowVersion(); e.HasIndex(x => new { x.RunnerId, x.IdempotencyKey }).IsUnique(); e.HasIndex(x => x.ProviderReference).IsUnique().HasFilter("[ProviderReference] <> ''"); });
        b.Entity<UserNotification>(e => { e.ToTable("Notifications", "notifications"); e.HasKey(x => x.Id); e.Property(x => x.Title).HasMaxLength(160); e.Property(x => x.Body).HasMaxLength(1000); e.HasIndex(x => new { x.RecipientId, x.CreatedAt }); e.HasIndex(x => new { x.RecipientId, x.ReadAt }); });
        b.Entity<Conversation>(e => { e.ToTable("Conversations", "communications"); e.HasKey(x => x.Id); e.HasIndex(x => x.ErrandId).IsUnique(); e.HasIndex(x => x.CustomerId); e.HasIndex(x => x.RunnerId); e.OwnsMany(x => x.Messages, m => { m.ToTable("Messages", "communications"); m.WithOwner().HasForeignKey("ConversationId"); m.HasKey(x => x.Id); m.Property(x => x.Body).HasMaxLength(2000); m.HasIndex("ConversationId", nameof(ChatMessage.SentAt)); }); });
        b.Entity<VoiceCallSession>(e => { e.ToTable("VoiceCalls", "communications"); e.HasKey(x => x.Id); e.Property(x => x.EndReason).HasMaxLength(240); e.HasIndex(x => new { x.ConversationId, x.CreatedAt }); e.HasIndex(x => new { x.CalleeId, x.Status }); });
    }
}
public sealed class ErrandRepository(ErrandRunsDbContext db) : IErrandRepository
{
    public async Task Add(Errand value, CancellationToken ct) => await db.Errands.AddAsync(value, ct);
    public Task<Errand?> Find(Guid id, CancellationToken ct) => db.Errands.Include(x => x.Stops).Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<Errand>> ListForUser(Guid id, bool runner, bool? active, int skip, int take, CancellationToken ct) => await Filter(id, runner, active).AsNoTracking().OrderByDescending(x => x.CreatedAt).Skip(skip).Take(Math.Min(take, 100)).ToListAsync(ct);
    public Task<int> CountForUser(Guid id, bool runner, bool? active, CancellationToken ct) => Filter(id, runner, active).CountAsync(ct);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
    private IQueryable<Errand> Filter(Guid id, bool runner, bool? active)
    {
        var query = db.Errands.Where(x => runner ? x.RunnerId == id : x.CustomerId == id);
        if (active is true) query = query.Where(x => x.Status != ErrandStatus.Completed && x.Status != ErrandStatus.Cancelled && x.Status != ErrandStatus.Failed);
        if (active is false) query = query.Where(x => x.Status == ErrandStatus.Completed || x.Status == ErrandStatus.Cancelled || x.Status == ErrandStatus.Failed);
        return query;
    }
}
public sealed class RunnerRepository(ErrandRunsDbContext db) : IRunnerRepository
{
    public Task<RunnerProfile?> Find(Guid id, CancellationToken ct) => db.Runners.SingleOrDefaultAsync(x => x.UserId == id, ct);
    public async Task<IReadOnlyList<RunnerProfile>> Available(CancellationToken ct) => await db.Runners.AsNoTracking().Where(x => x.Status == RunnerStatus.Available).Take(50).ToListAsync(ct);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
public sealed class RunnerFinanceRepository(ErrandRunsDbContext db) : IRunnerFinanceRepository
{
    public Task<RunnerPayoutAccount?> GetPayoutAccount(Guid runnerId, CancellationToken ct) => db.RunnerPayoutAccounts.SingleOrDefaultAsync(x => x.RunnerId == runnerId, ct);
    public void AddPayoutAccount(RunnerPayoutAccount account) => db.RunnerPayoutAccounts.Add(account);
    public Task<bool> HasEarning(Guid errandId, CancellationToken ct) => db.RunnerLedger.AnyAsync(x => x.ErrandId == errandId, ct);
    public void AddLedgerEntry(RunnerLedgerEntry entry) => db.RunnerLedger.Add(entry);
    public async Task<decimal> Balance(Guid runnerId, string currency, CancellationToken ct)
    {
        var credits = await db.RunnerLedger.Where(x => x.RunnerId == runnerId && x.Currency == currency && (x.Type == RunnerLedgerEntryType.Earning || x.Type == RunnerLedgerEntryType.PayoutReversal)).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var debits = await db.RunnerLedger.Where(x => x.RunnerId == runnerId && x.Currency == currency && x.Type == RunnerLedgerEntryType.Payout).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        return credits - debits;
    }
    public async Task<IReadOnlyList<RunnerLedgerEntry>> ListLedger(Guid runnerId, int skip, int take, CancellationToken ct) => await db.RunnerLedger.AsNoTracking().Where(x => x.RunnerId == runnerId).OrderByDescending(x => x.CreatedAt).Skip(skip).Take(Math.Min(take, 100)).ToListAsync(ct);
    public Task<int> CountLedger(Guid runnerId, CancellationToken ct) => db.RunnerLedger.CountAsync(x => x.RunnerId == runnerId, ct);
    public Task<RunnerPayout?> FindPayout(Guid runnerId, string idempotencyKey, CancellationToken ct) => db.RunnerPayouts.SingleOrDefaultAsync(x => x.RunnerId == runnerId && x.IdempotencyKey == idempotencyKey, ct);
    public Task<RunnerPayout?> FindPayoutByReference(string providerReference, CancellationToken ct) => db.RunnerPayouts.SingleOrDefaultAsync(x => x.ProviderReference == providerReference, ct);
    public Task<bool> HasLedgerEntry(Guid payoutId, RunnerLedgerEntryType type, CancellationToken ct) => db.RunnerLedger.AnyAsync(x => x.PayoutId == payoutId && x.Type == type, ct);
    public void AddPayout(RunnerPayout payout) => db.RunnerPayouts.Add(payout);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
public sealed class CommunicationRepository(ErrandRunsDbContext db):ICommunicationRepository
{
    public async Task AddNotification(UserNotification value,CancellationToken ct)=>await db.Notifications.AddAsync(value,ct);
    public async Task<IReadOnlyList<UserNotification>> ListNotifications(Guid id,bool? unread,int skip,int take,CancellationToken ct){var q=db.Notifications.AsNoTracking().Where(x=>x.RecipientId==id);if(unread is true)q=q.Where(x=>x.ReadAt==null);if(unread is false)q=q.Where(x=>x.ReadAt!=null);return await q.OrderByDescending(x=>x.CreatedAt).Skip(skip).Take(Math.Min(take,100)).ToListAsync(ct);}
    public Task<int> CountNotifications(Guid id,bool? unread,CancellationToken ct){var q=db.Notifications.Where(x=>x.RecipientId==id);if(unread is true)q=q.Where(x=>x.ReadAt==null);if(unread is false)q=q.Where(x=>x.ReadAt!=null);return q.CountAsync(ct);}
    public Task<UserNotification?> FindNotification(Guid id,CancellationToken ct)=>db.Notifications.SingleOrDefaultAsync(x=>x.Id==id,ct);
    public Task<Conversation?> FindConversation(Guid id,CancellationToken ct)=>db.Conversations.Include(x=>x.Messages).SingleOrDefaultAsync(x=>x.Id==id,ct);
    public Task<Conversation?> FindConversationForErrand(Guid id,CancellationToken ct)=>db.Conversations.Include(x=>x.Messages).SingleOrDefaultAsync(x=>x.ErrandId==id,ct);
    public async Task AddConversation(Conversation value,CancellationToken ct)=>await db.Conversations.AddAsync(value,ct);
    public async Task<IReadOnlyList<Conversation>> ListConversations(Guid id,int skip,int take,CancellationToken ct)=>await db.Conversations.AsNoTracking().Include(x=>x.Messages).Where(x=>x.CustomerId==id||x.RunnerId==id).OrderByDescending(x=>x.CreatedAt).Skip(skip).Take(Math.Min(take,100)).ToListAsync(ct);
    public Task<int> CountConversations(Guid id,CancellationToken ct)=>db.Conversations.CountAsync(x=>x.CustomerId==id||x.RunnerId==id,ct);
    public Task<VoiceCallSession?> FindCall(Guid id,CancellationToken ct)=>db.VoiceCalls.SingleOrDefaultAsync(x=>x.Id==id,ct);
    public async Task AddCall(VoiceCallSession value,CancellationToken ct)=>await db.VoiceCalls.AddAsync(value,ct);
    public Task Save(CancellationToken ct)=>db.SaveChangesAsync(ct);
}
