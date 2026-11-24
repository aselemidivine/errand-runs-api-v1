using ErrandRuns.Application;
using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Errands;
using ErrandRuns.Domain.Communications;
using ErrandRuns.Domain.Payments;
using ErrandRuns.Domain.Runners;
using ErrandRuns.Domain.Reviews;
using ErrandRuns.Domain.Users;
using ErrandRuns.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace ErrandRuns.Infrastructure;

public sealed class ErrandRunsDbContext(DbContextOptions<ErrandRunsDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Errand> Errands => Set<Errand>(); public DbSet<RunnerProfile> Runners => Set<RunnerProfile>(); public DbSet<RunnerVerification> RunnerVerifications => Set<RunnerVerification>(); public DbSet<RunnerDeclinedOffer> RunnerDeclinedOffers => Set<RunnerDeclinedOffer>(); public DbSet<Payment> Payments => Set<Payment>(); public DbSet<RunnerLedgerEntry> RunnerLedger => Set<RunnerLedgerEntry>(); public DbSet<RunnerPayoutAccount> RunnerPayoutAccounts => Set<RunnerPayoutAccount>(); public DbSet<RunnerPayout> RunnerPayouts => Set<RunnerPayout>(); public DbSet<UserNotification> Notifications => Set<UserNotification>(); public DbSet<Conversation> Conversations => Set<Conversation>(); public DbSet<VoiceCallSession> VoiceCalls => Set<VoiceCallSession>(); public DbSet<SavedLocation> SavedLocations => Set<SavedLocation>(); public DbSet<PhoneVerificationChallenge> PhoneVerificationChallenges => Set<PhoneVerificationChallenge>(); public DbSet<ErrandReview> Reviews => Set<ErrandReview>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema("app");
        // Keep identity data separate from operational errand data. This also
        // makes retention and access policies easier to apply independently.
        b.Entity<ApplicationUser>(e => { e.ToTable("Users", "identity"); e.Property(x => x.DisplayName).HasMaxLength(120); e.Property(x => x.PhoneNumber).HasMaxLength(32); e.Property(x => x.Bio).HasMaxLength(300); e.Property(x => x.ProfilePictureContentType).HasMaxLength(32); e.HasIndex(x => x.PhoneNumber).IsUnique().HasFilter("[PhoneNumber] IS NOT NULL"); });
        b.Entity<IdentityRole<Guid>>().ToTable("Roles", "identity");
        b.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles", "identity");
        b.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims", "identity");
        b.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins", "identity");
        b.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims", "identity");
        b.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens", "identity");
        b.Entity<PhoneVerificationChallenge>(e => { e.ToTable("PhoneVerificationChallenges", "identity"); e.HasKey(x => x.Id); e.Property(x => x.PhoneNumber).HasMaxLength(32); e.HasIndex(x => new { x.UserId, x.SentAt }); e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<SavedLocation>(e => { e.ToTable("SavedLocations", "identity"); e.HasKey(x => x.Id); e.Property(x => x.Label).HasMaxLength(60); e.Property(x => x.Address).HasMaxLength(500); e.Property(x => x.Latitude).HasPrecision(9, 6); e.Property(x => x.Longitude).HasPrecision(9, 6); e.Property(x => x.Landmark).HasMaxLength(240); e.Property(x => x.DeliveryInstructions).HasMaxLength(1000); e.Property(x => x.GooglePlaceId).HasMaxLength(255); e.Property(x => x.AddressComponentsJson).HasMaxLength(16000); e.HasIndex(x => new { x.UserId, x.UpdatedAt }); e.HasIndex(x => new { x.UserId, x.IsDefault }).IsUnique().HasFilter("[IsDefault] = 1"); e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); e.HasMany(x => x.PreferredCategories).WithOne().HasForeignKey(x => x.SavedLocationId).OnDelete(DeleteBehavior.Cascade); e.Navigation(x => x.PreferredCategories).UsePropertyAccessMode(PropertyAccessMode.Field); });
        b.Entity<SavedLocationCategory>(e => { e.ToTable("SavedLocationCategories", "identity"); e.HasKey(x => new { x.SavedLocationId, x.Category }); });
        b.Entity<Errand>(e => { e.ToTable("Errands"); e.HasKey(x => x.Id); e.Property(x => x.Title).HasMaxLength(160); e.Property(x => x.PreferredProvider).HasMaxLength(160); e.Property(x => x.SpecialInstructions).HasMaxLength(1000); e.Property(x => x.CompletionCodeHash).HasMaxLength(128); e.Property(x => x.MerchandiseEstimate).HasPrecision(18, 2); e.Property(x => x.ServiceFee).HasPrecision(18, 2); e.Property(x => x.Currency).HasMaxLength(3); e.Ignore(x => x.TotalEstimate); e.Property(x => x.RowVersion).IsRowVersion(); e.HasIndex(x => new { x.CustomerId, x.CreatedAt }); e.OwnsMany(x => x.Stops, s => { s.ToTable("ErrandStops"); s.WithOwner().HasForeignKey("ErrandId"); s.HasKey(x => x.Id); s.HasIndex("ErrandId", nameof(ErrandStop.Sequence)).IsUnique(); s.Property(x => x.Address).HasMaxLength(500); s.OwnsOne(x => x.Location, g => { g.Property(x => x.Latitude).HasPrecision(9, 6); g.Property(x => x.Longitude).HasPrecision(9, 6); }); }); e.OwnsMany(x => x.Items, i => { i.ToTable("ErrandItems"); i.WithOwner().HasForeignKey("ErrandId"); i.HasKey(x => x.Id); i.Property(x => x.Name).HasMaxLength(160); i.Property(x => x.Unit).HasMaxLength(40); i.Property(x => x.EstimatedUnitPrice).HasPrecision(18, 2); }); });
        b.Entity<RunnerProfile>(e => { e.ToTable("RunnerProfiles", "runners"); e.HasKey(x => x.UserId); e.Property(x => x.Rating).HasPrecision(3, 2); e.Property(x => x.CurrentLatitude).HasPrecision(9, 6); e.Property(x => x.CurrentLongitude).HasPrecision(9, 6); e.HasIndex(x => x.Status); e.HasIndex(x => new { x.Status, x.LocationUpdatedAt, x.CurrentLatitude, x.CurrentLongitude }); e.HasOne<ApplicationUser>().WithOne().HasForeignKey<RunnerProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<RunnerDeclinedOffer>(e => { e.ToTable("RunnerDeclinedOffers", "runners"); e.HasKey(x => new { x.RunnerId, x.ErrandId }); e.HasIndex(x => new { x.RunnerId, x.DeclinedAt }); e.HasOne<RunnerProfile>().WithMany().HasForeignKey(x => x.RunnerId).OnDelete(DeleteBehavior.Cascade); e.HasOne<Errand>().WithMany().HasForeignKey(x => x.ErrandId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<RunnerVerification>(e => { e.ToTable("RunnerVerifications", "runners"); e.HasKey(x => x.RunnerId); e.Property(x => x.IdentityDocumentKey).HasMaxLength(500); e.Property(x => x.VehiclePlateNumber).HasMaxLength(32); e.Property(x => x.VehicleMakeModel).HasMaxLength(160); e.Property(x => x.OwnershipDocumentKey).HasMaxLength(500); e.Property(x => x.RejectionReason).HasMaxLength(500); e.Property(x => x.RowVersion).IsRowVersion(); e.HasIndex(x => new { x.Status, x.SubmittedAt }); e.HasOne<RunnerProfile>().WithOne().HasForeignKey<RunnerVerification>(x => x.RunnerId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<Payment>(e => { e.ToTable("Payments", "payments"); e.HasKey(x => x.Id); e.Property(x => x.RowVersion).IsRowVersion(); e.Property(x => x.IdempotencyKey).HasMaxLength(120); e.Property(x => x.ProviderReference).HasMaxLength(160); e.Property(x => x.Provider).HasMaxLength(40); e.Property(x => x.PaymentMethod).HasMaxLength(40); e.Property(x => x.CheckoutUrl).HasMaxLength(2048); e.Property(x => x.AccessCode).HasMaxLength(160); e.HasIndex(x => x.IdempotencyKey).IsUnique(); e.HasIndex(x => x.ErrandId); e.HasIndex(x => x.ProviderReference).IsUnique().HasFilter("[ProviderReference] <> ''"); e.ComplexProperty(x => x.Amount, m => { m.Property(x => x.Amount).HasColumnName("Amount").HasPrecision(18, 2); m.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3); }); });
        b.Entity<RunnerLedgerEntry>(e => { e.ToTable("RunnerLedger", "payments"); e.HasKey(x => x.Id); e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.Currency).HasMaxLength(3); e.Property(x => x.Description).HasMaxLength(240); e.HasIndex(x => x.RunnerId); e.HasIndex(x => x.ErrandId).IsUnique().HasFilter("[ErrandId] IS NOT NULL"); e.HasIndex(x => new { x.PayoutId, x.Type }).IsUnique().HasFilter("[PayoutId] IS NOT NULL"); });
        b.Entity<RunnerPayoutAccount>(e => { e.ToTable("RunnerPayoutAccounts", "payments"); e.HasKey(x => x.RunnerId); e.Property(x => x.BankCode).HasMaxLength(20); e.Property(x => x.BankName).HasMaxLength(120); e.Property(x => x.AccountName).HasMaxLength(160); e.Property(x => x.AccountNumberLast4).HasMaxLength(4); e.Property(x => x.RecipientCode).HasMaxLength(120); });
        b.Entity<RunnerPayout>(e => { e.ToTable("RunnerPayouts", "payments"); e.HasKey(x => x.Id); e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.Fee).HasPrecision(18, 2); e.Property(x => x.Currency).HasMaxLength(3); e.Property(x => x.IdempotencyKey).HasMaxLength(120); e.Property(x => x.ProviderReference).HasMaxLength(160); e.Property(x => x.FailureReason).HasMaxLength(500); e.Property(x => x.RowVersion).IsRowVersion(); e.HasIndex(x => new { x.RunnerId, x.IdempotencyKey }).IsUnique(); e.HasIndex(x => x.ProviderReference).IsUnique().HasFilter("[ProviderReference] <> ''"); });
        b.Entity<UserNotification>(e => { e.ToTable("Notifications", "notifications"); e.HasKey(x => x.Id); e.Property(x => x.Title).HasMaxLength(160); e.Property(x => x.Body).HasMaxLength(1000); e.HasIndex(x => new { x.RecipientId, x.CreatedAt }); e.HasIndex(x => new { x.RecipientId, x.ReadAt }); });
        b.Entity<Conversation>(e => { e.ToTable("Conversations", "communications"); e.HasKey(x => x.Id); e.HasIndex(x => x.ErrandId).IsUnique(); e.HasIndex(x => x.CustomerId); e.HasIndex(x => x.RunnerId); e.OwnsMany(x => x.Messages, m => { m.ToTable("Messages", "communications"); m.WithOwner().HasForeignKey("ConversationId"); m.HasKey(x => x.Id); m.Property(x => x.Body).HasMaxLength(2000); m.HasIndex("ConversationId", nameof(ChatMessage.SentAt)); }); });
        b.Entity<VoiceCallSession>(e => { e.ToTable("VoiceCalls", "communications"); e.HasKey(x => x.Id); e.Property(x => x.EndReason).HasMaxLength(240); e.HasIndex(x => new { x.ConversationId, x.CreatedAt }); e.HasIndex(x => new { x.CalleeId, x.Status }); });
        b.Entity<ErrandReview>(e=>{e.ToTable("ErrandReviews","reviews");e.HasKey(x=>x.Id);e.Property(x=>x.Comment).HasMaxLength(1000);e.HasIndex(x=>new{x.ErrandId,x.ReviewerId}).IsUnique();e.HasIndex(x=>new{x.RevieweeId,x.CreatedAt});e.HasOne<Errand>().WithMany().HasForeignKey(x=>x.ErrandId).OnDelete(DeleteBehavior.Cascade);e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ReviewerId).OnDelete(DeleteBehavior.NoAction);e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.RevieweeId).OnDelete(DeleteBehavior.NoAction);});
    }
}

public sealed class ReviewRepository(ErrandRunsDbContext db):IReviewRepository
{
    public Task<ErrandReview?> Find(Guid errandId,Guid reviewerId,CancellationToken ct)=>db.Reviews.SingleOrDefaultAsync(x=>x.ErrandId==errandId&&x.ReviewerId==reviewerId,ct);
    public async Task<IReadOnlyList<ReviewListItem>> ListForUser(Guid userId,int skip,int take,CancellationToken ct)=>await db.Reviews.AsNoTracking().Where(x=>x.RevieweeId==userId).Join(db.Users.AsNoTracking(),x=>x.ReviewerId,u=>u.Id,(x,u)=>new ReviewListItem(x.Id,x.ErrandId,x.ReviewerId,u.DisplayName,x.Rating,x.Comment==""?null:x.Comment,x.CreatedAt)).OrderByDescending(x=>x.CreatedAt).Skip(skip).Take(Math.Clamp(take,1,100)).ToListAsync(ct);
    public Task<int> CountForUser(Guid userId,CancellationToken ct)=>db.Reviews.CountAsync(x=>x.RevieweeId==userId,ct);
    public async Task<RatingSummary> RatingForUser(Guid userId,CancellationToken ct){var q=db.Reviews.Where(x=>x.RevieweeId==userId);var count=await q.CountAsync(ct);var average=count==0?0m:await q.AverageAsync(x=>(decimal)x.Rating,ct);return new(Math.Round(average,2),count);}
    public void Add(ErrandReview review)=>db.Reviews.Add(review);
    public Task Save(CancellationToken ct)=>db.SaveChangesAsync(ct);
}
public sealed class ErrandRepository(ErrandRunsDbContext db) : IErrandRepository
{
    public async Task Add(Errand value, CancellationToken ct) => await db.Errands.AddAsync(value, ct);
    public Task<Errand?> Find(Guid id, CancellationToken ct) => db.Errands.Include(x => x.Stops).Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<Errand>> ListForUser(Guid id, bool runner, bool? active, int skip, int take, CancellationToken ct) => await Filter(id, runner, active).AsNoTracking().Include(x => x.Stops).OrderByDescending(x => x.CreatedAt).Skip(skip).Take(Math.Min(take, 100)).ToListAsync(ct);
    public Task<int> CountForUser(Guid id, bool runner, bool? active, CancellationToken ct) => Filter(id, runner, active).CountAsync(ct);
    public async Task<IReadOnlyList<Errand>> SearchForCustomer(Guid customerId, string query, int skip, int take, CancellationToken ct) => await Search(customerId, query).AsNoTracking().Include(x => x.Stops).OrderByDescending(x => x.CreatedAt).Skip(skip).Take(Math.Min(take, 100)).ToListAsync(ct);
    public Task<int> CountSearchForCustomer(Guid customerId, string query, CancellationToken ct) => Search(customerId, query).CountAsync(ct);
    public async Task<IReadOnlyList<Errand>> ListAvailableOffers(int take, CancellationToken ct) =>
        await db.Errands.AsNoTracking().Include(x => x.Stops).Include(x => x.Items)
            .Where(x => x.Status == ErrandStatus.PaymentConfirmed && x.RunnerId == null && x.Stops.Count > 0)
            .OrderBy(x => x.ScheduledFor ?? x.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct);
    public async Task<IReadOnlyList<Errand>> ListAvailableOffers(decimal minLatitude,
        decimal maxLatitude, decimal minLongitude, decimal maxLongitude,
        int take, CancellationToken ct) =>
        await db.Errands.AsNoTracking().Include(x => x.Stops).Include(x => x.Items)
            .Where(x => x.Status == ErrandStatus.PaymentConfirmed && x.RunnerId == null &&
                x.Stops.Count > 0 && x.Stops.OrderBy(stop => stop.Sequence).First().Location.Latitude >= minLatitude &&
                x.Stops.OrderBy(stop => stop.Sequence).First().Location.Latitude <= maxLatitude &&
                x.Stops.OrderBy(stop => stop.Sequence).First().Location.Longitude >= minLongitude &&
                x.Stops.OrderBy(stop => stop.Sequence).First().Location.Longitude <= maxLongitude)
            .OrderBy(x => x.ScheduledFor ?? x.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct);
    public void Remove(Errand value) => db.Errands.Remove(value);
    public async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { throw new ConcurrencyConflictException("The resource was updated by another request."); }
    }
    private IQueryable<Errand> Filter(Guid id, bool runner, bool? active)
    {
        var query = db.Errands.Where(x => runner ? x.RunnerId == id : x.CustomerId == id);
        if (active is true) query = query.Where(x => x.Status != ErrandStatus.Completed && x.Status != ErrandStatus.Cancelled && x.Status != ErrandStatus.Failed);
        if (active is false) query = query.Where(x => x.Status == ErrandStatus.Completed || x.Status == ErrandStatus.Cancelled || x.Status == ErrandStatus.Failed);
        return query;
    }
    private IQueryable<Errand> Search(Guid customerId, string query)
    {
        var normalized = query.ToLower();
        return db.Errands.Where(x =>
            x.CustomerId == customerId &&
            (x.Title.ToLower().Contains(normalized) ||
             (x.PreferredProvider != null && x.PreferredProvider.ToLower().Contains(normalized)) ||
             (x.SpecialInstructions != null && x.SpecialInstructions.ToLower().Contains(normalized)) ||
             x.Stops.Any(stop => stop.Address.ToLower().Contains(normalized) ||
                 (stop.Instructions != null && stop.Instructions.ToLower().Contains(normalized))) ||
             x.Items.Any(item => item.Name.ToLower().Contains(normalized))));
    }
}
public sealed class RunnerVerificationRepository(ErrandRunsDbContext db) : IRunnerVerificationRepository
{
    public Task<RunnerVerification?> Find(Guid runnerId, CancellationToken ct) =>
        db.RunnerVerifications.SingleOrDefaultAsync(x => x.RunnerId == runnerId, ct);
    public async Task<IReadOnlyList<RunnerVerification>> Pending(int skip, int take, CancellationToken ct) =>
        await db.RunnerVerifications.AsNoTracking()
            .Where(x => x.Status == VerificationStatus.Submitted || x.Status == VerificationStatus.UnderReview)
            .OrderBy(x => x.SubmittedAt).Skip(skip).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
    public Task<int> CountPending(CancellationToken ct) => db.RunnerVerifications.CountAsync(
        x => x.Status == VerificationStatus.Submitted || x.Status == VerificationStatus.UnderReview, ct);
    public void Add(RunnerVerification verification) => db.RunnerVerifications.Add(verification);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
public sealed class RunnerRepository(ErrandRunsDbContext db) : IRunnerRepository
{
    public Task<RunnerProfile?> Find(Guid id, CancellationToken ct) => db.Runners.SingleOrDefaultAsync(x => x.UserId == id, ct);
    public async Task<IReadOnlyList<RunnerProfile>> Available(CancellationToken ct) => await db.Runners.AsNoTracking().Where(x => x.Status == RunnerStatus.Available).Take(50).ToListAsync(ct);
    public async Task<IReadOnlyList<NearbyRunnerCandidate>> SearchNearby(decimal minLatitude,
        decimal maxLatitude, decimal minLongitude, decimal maxLongitude,
        DateTimeOffset locationFreshAfter, int take, CancellationToken ct) =>
        await db.Runners.AsNoTracking().Join(db.Users.AsNoTracking(), runner => runner.UserId,
                user => user.Id, (runner, user) => new { runner, user })
            .Where(value => value.runner.Status == RunnerStatus.Available &&
                value.runner.LocationUpdatedAt >= locationFreshAfter &&
                value.runner.CurrentLatitude >= minLatitude && value.runner.CurrentLatitude <= maxLatitude &&
                value.runner.CurrentLongitude >= minLongitude && value.runner.CurrentLongitude <= maxLongitude)
            .OrderByDescending(value => value.runner.Rating)
            .Take(Math.Clamp(take, 1, 1000))
            .Select(value => new NearbyRunnerCandidate(value.runner.UserId,
                value.user.DisplayName, value.runner.CurrentLatitude!.Value,
                value.runner.CurrentLongitude!.Value, value.runner.Rating,
                value.runner.CompletedErrands, value.runner.LocationUpdatedAt!.Value,
                value.user.ProfilePictureData != null))
            .ToListAsync(ct);
    public async Task<IReadOnlySet<Guid>> DeclinedOfferIds(Guid runnerId, CancellationToken ct) =>
        (await db.RunnerDeclinedOffers.AsNoTracking().Where(x => x.RunnerId == runnerId)
            .Select(x => x.ErrandId).ToListAsync(ct)).ToHashSet();
    public Task<bool> HasDeclinedOffer(Guid runnerId, Guid errandId, CancellationToken ct) =>
        db.RunnerDeclinedOffers.AnyAsync(x => x.RunnerId == runnerId && x.ErrandId == errandId, ct);
    public void AddDeclinedOffer(RunnerDeclinedOffer decline) => db.RunnerDeclinedOffers.Add(decline);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
public sealed class PaymentRepository(ErrandRunsDbContext db) : IPaymentRepository
{
    public Task<Payment?> Find(Guid paymentId, CancellationToken ct) =>
        db.Payments.SingleOrDefaultAsync(x => x.Id == paymentId, ct);
    public Task<Payment?> FindByReference(string providerReference, CancellationToken ct) =>
        db.Payments.SingleOrDefaultAsync(x => x.ProviderReference == providerReference, ct);
    public Task<Payment?> FindByIdempotencyKey(string idempotencyKey, CancellationToken ct) =>
        db.Payments.SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);
    public Task<Payment?> FindCurrentForErrand(Guid errandId, CancellationToken ct) =>
        db.Payments.OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(
            x => x.ErrandId == errandId &&
                (x.Status == PaymentStatus.Pending ||
                 x.Status == PaymentStatus.Authorized ||
                 x.Status == PaymentStatus.Confirmed), ct);
    public async Task Add(Payment payment, CancellationToken ct) =>
        await db.Payments.AddAsync(payment, ct);
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
public sealed class UserPreferenceRepository(ErrandRunsDbContext db) : IUserPreferenceRepository
{
    public Task<int> CountLocations(Guid userId, CancellationToken ct) => db.SavedLocations.CountAsync(x => x.UserId == userId, ct);
    public async Task<IReadOnlyList<SavedLocation>> ListLocations(Guid userId, CancellationToken ct) => await db.SavedLocations.Include(x => x.PreferredCategories).Where(x => x.UserId == userId).OrderByDescending(x => x.IsDefault).ThenByDescending(x => x.IsFavorite).ThenByDescending(x => x.UpdatedAt).ToListAsync(ct);
    public async Task<IReadOnlyList<SavedLocation>> ListLocations(Guid userId,int skip,int take,CancellationToken ct)=>await db.SavedLocations.AsNoTracking().Include(x=>x.PreferredCategories).Where(x=>x.UserId==userId).OrderByDescending(x=>x.IsDefault).ThenByDescending(x=>x.IsFavorite).ThenByDescending(x=>x.UpdatedAt).Skip(skip).Take(Math.Clamp(take,1,100)).ToListAsync(ct);
    public Task<SavedLocation?> FindLocation(Guid id, CancellationToken ct) => db.SavedLocations.Include(x => x.PreferredCategories).SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<SavedLocation?> FindDefaultLocation(Guid userId, CancellationToken ct) => db.SavedLocations.Include(x => x.PreferredCategories).SingleOrDefaultAsync(x => x.UserId == userId && x.IsDefault, ct);
    public void AddLocation(SavedLocation location) => db.SavedLocations.Add(location);
    public void RemoveLocation(SavedLocation location) => db.SavedLocations.Remove(location);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
public sealed class CommunicationRepository(ErrandRunsDbContext db):ICommunicationRepository
{
    // Client-generated GUIDs on new owned messages must be inserted, not inferred as existing rows.
    public void TrackNewMessage(ChatMessage message) => db.Entry(message).State = EntityState.Added;
    public async Task AddNotification(UserNotification value,CancellationToken ct)=>await db.Notifications.AddAsync(value,ct);
    public async Task<IReadOnlyList<UserNotification>> ListNotifications(Guid id,bool? unread,int skip,int take,CancellationToken ct){var q=db.Notifications.AsNoTracking().Where(x=>x.RecipientId==id);if(unread is true)q=q.Where(x=>x.ReadAt==null);if(unread is false)q=q.Where(x=>x.ReadAt!=null);return await q.OrderByDescending(x=>x.CreatedAt).Skip(skip).Take(Math.Min(take,100)).ToListAsync(ct);}
    public Task<int> CountNotifications(Guid id,bool? unread,CancellationToken ct){var q=db.Notifications.Where(x=>x.RecipientId==id);if(unread is true)q=q.Where(x=>x.ReadAt==null);if(unread is false)q=q.Where(x=>x.ReadAt!=null);return q.CountAsync(ct);}
    public Task<UserNotification?> FindNotification(Guid id,CancellationToken ct)=>db.Notifications.SingleOrDefaultAsync(x=>x.Id==id,ct);
    public Task<Conversation?> FindConversation(Guid id,CancellationToken ct)=>db.Conversations.Include(x=>x.Messages).SingleOrDefaultAsync(x=>x.Id==id,ct);
    public Task<Conversation?> FindConversationForErrand(Guid id,CancellationToken ct)=>db.Conversations.Include(x=>x.Messages).SingleOrDefaultAsync(x=>x.ErrandId==id,ct);
    public async Task AddConversation(Conversation value,CancellationToken ct)=>await db.Conversations.AddAsync(value,ct);
    public async Task<IReadOnlyList<Conversation>> ListConversations(Guid id,int skip,int take,CancellationToken ct)=>await db.Conversations.AsNoTracking().Include(x=>x.Messages).Where(x=>x.CustomerId==id||x.RunnerId==id).OrderByDescending(x=>x.CreatedAt).Skip(skip).Take(Math.Min(take,100)).ToListAsync(ct);
    public Task<int> CountConversations(Guid id,CancellationToken ct)=>db.Conversations.CountAsync(x=>x.CustomerId==id||x.RunnerId==id,ct);
    public async Task<IReadOnlyList<ConversationParticipantProfile>> GetConversationParticipants(IReadOnlyCollection<Guid> ids,CancellationToken ct)
    {
        var users=await db.Users.AsNoTracking().Where(x=>ids.Contains(x.Id)).Select(x=>new{x.Id,x.DisplayName,HasProfilePicture=x.ProfilePictureData!=null}).ToListAsync(ct);
        var runners=await db.Runners.AsNoTracking().Where(x=>ids.Contains(x.UserId)).ToDictionaryAsync(x=>x.UserId,ct);
        var ratings=await db.Reviews.AsNoTracking().Where(x=>ids.Contains(x.RevieweeId)).GroupBy(x=>x.RevieweeId).Select(x=>new{UserId=x.Key,Rating=x.Average(y=>(decimal)y.Rating),Count=x.Count()}).ToDictionaryAsync(x=>x.UserId,ct);
        return users.Select(x=>{ratings.TryGetValue(x.Id,out var a);return runners.TryGetValue(x.Id,out var r)?new ConversationParticipantProfile(x.Id,x.DisplayName,x.HasProfilePicture,r.Status,a is null?0:Math.Round(a.Rating,2),a?.Count??0,r.CompletedErrands):new ConversationParticipantProfile(x.Id,x.DisplayName,x.HasProfilePicture,null,a is null?0:Math.Round(a.Rating,2),a?.Count??0,null);}).ToList();
    }
    public Task<VoiceCallSession?> FindCall(Guid id,CancellationToken ct)=>db.VoiceCalls.SingleOrDefaultAsync(x=>x.Id==id,ct);
    public async Task AddCall(VoiceCallSession value,CancellationToken ct)=>await db.VoiceCalls.AddAsync(value,ct);
    public async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("This conversation changed while your request was being saved. Retry using the same clientMessageId.");
        }
    }
}
