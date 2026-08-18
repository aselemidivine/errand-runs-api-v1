using ErrandRuns.Application;
using ErrandRuns.Domain.Errands;
using ErrandRuns.Domain.Payments;
using ErrandRuns.Domain.Runners;
using ErrandRuns.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace ErrandRuns.Infrastructure;

public sealed class ErrandRunsDbContext(DbContextOptions<ErrandRunsDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Errand> Errands => Set<Errand>(); public DbSet<RunnerProfile> Runners => Set<RunnerProfile>(); public DbSet<Payment> Payments => Set<Payment>();
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
        b.Entity<Errand>(e => { e.ToTable("Errands"); e.HasKey(x => x.Id); e.Property(x => x.Title).HasMaxLength(160); e.Property(x => x.RowVersion).IsRowVersion(); e.HasIndex(x => new { x.CustomerId, x.CreatedAt }); e.OwnsMany(x => x.Stops, s => { s.ToTable("ErrandStops"); s.WithOwner().HasForeignKey("ErrandId"); s.HasKey(x => x.Id); s.HasIndex("ErrandId", nameof(ErrandStop.Sequence)).IsUnique(); s.Property(x => x.Address).HasMaxLength(500); s.OwnsOne(x => x.Location, g => { g.Property(x => x.Latitude).HasPrecision(9, 6); g.Property(x => x.Longitude).HasPrecision(9, 6); }); }); });
        b.Entity<RunnerProfile>(e => { e.ToTable("RunnerProfiles", "runners"); e.HasKey(x => x.UserId); e.Property(x => x.Rating).HasPrecision(3, 2); e.HasIndex(x => x.Status); e.HasOne<ApplicationUser>().WithOne().HasForeignKey<RunnerProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<Payment>(e => { e.ToTable("Payments", "payments"); e.HasKey(x => x.Id); e.Property(x => x.RowVersion).IsRowVersion(); e.HasIndex(x => x.IdempotencyKey).IsUnique(); e.HasIndex(x => x.ProviderReference).IsUnique().HasFilter("[ProviderReference] <> ''"); e.ComplexProperty(x => x.Amount, m => { m.Property(x => x.Amount).HasColumnName("Amount").HasPrecision(18, 2); m.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3); }); });
    }
}
public sealed class ErrandRepository(ErrandRunsDbContext db) : IErrandRepository
{
    public async Task Add(Errand value, CancellationToken ct) => await db.Errands.AddAsync(value, ct);
    public Task<Errand?> Find(Guid id, CancellationToken ct) => db.Errands.Include(x => x.Stops).SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<Errand>> ListForUser(Guid id, bool runner, int skip, int take, CancellationToken ct) => await db.Errands.AsNoTracking().Where(x => runner ? x.RunnerId == id : x.CustomerId == id).OrderByDescending(x => x.CreatedAt).Skip(skip).Take(Math.Min(take, 100)).ToListAsync(ct);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
public sealed class RunnerRepository(ErrandRunsDbContext db) : IRunnerRepository
{
    public Task<RunnerProfile?> Find(Guid id, CancellationToken ct) => db.Runners.SingleOrDefaultAsync(x => x.UserId == id, ct);
    public async Task<IReadOnlyList<RunnerProfile>> Available(CancellationToken ct) => await db.Runners.AsNoTracking().Where(x => x.Status == RunnerStatus.Available).Take(50).ToListAsync(ct);
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
}
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
