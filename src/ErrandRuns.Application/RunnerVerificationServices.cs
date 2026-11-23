using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Errands;
using ErrandRuns.Domain.Runners;

namespace ErrandRuns.Application;

public sealed class RunnerVerificationService(
    IRunnerVerificationRepository verifications,
    IRunnerRepository runners,
    IPrivateDocumentStorage storage,
    INotificationPublisher notifications,
    ICurrentUser current,
    IClock clock)
{
    public async Task<RunnerVerificationDetails> Get(CancellationToken ct) =>
        Map(await GetOrCreate(ct));

    public async Task<RunnerVerificationDetails> SetIdentity(
        SubmitIdentityVerification request, CancellationToken ct)
    {
        var verification = await GetOrCreate(ct);
        var oldKey = verification.IdentityDocumentKey;
        var key = await storage.Save(current.UserId, "government-id", request.Document, ct);
        try
        {
            verification.SetIdentity(request.DocumentType, key);
            await verifications.Save(ct);
        }
        catch
        {
            await storage.Delete(key, ct);
            throw;
        }
        if (!string.IsNullOrWhiteSpace(oldKey) && oldKey != key) await storage.Delete(oldKey, ct);
        return Map(verification);
    }

    public async Task<RunnerVerificationDetails> SetVehicle(
        SubmitVehicleVerification request, CancellationToken ct)
    {
        var verification = await GetOrCreate(ct);
        var oldKey = verification.OwnershipDocumentKey;
        string? key = null;
        if (request.OwnershipDocument is not null)
            key = await storage.Save(current.UserId, "vehicle-ownership", request.OwnershipDocument, ct);
        try
        {
            verification.SetVehicle(request.VehicleType, request.PlateNumber,
                request.MakeModel, key ?? oldKey);
            await verifications.Save(ct);
        }
        catch
        {
            if (key is not null) await storage.Delete(key, ct);
            throw;
        }
        if (key is not null && !string.IsNullOrWhiteSpace(oldKey) && oldKey != key)
            await storage.Delete(oldKey, ct);
        return Map(verification);
    }

    public async Task<RunnerVerificationDetails> Submit(CancellationToken ct)
    {
        var verification = await GetOrCreate(ct);
        var runner = await Runner(ct);
        verification.Submit(clock.UtcNow);
        runner.SubmitVerification();
        await verifications.Save(ct);
        await notifications.Publish(current.UserId, Domain.Communications.NotificationType.System,
            "Verification submitted",
            "Your runner verification was submitted and is waiting for review.", null, ct);
        return Map(verification);
    }

    public async Task<PagedRunnerVerifications> Pending(int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await verifications.CountPending(ct);
        var values = await verifications.Pending((page - 1) * pageSize, pageSize, ct);
        return new(values.Select(Map).ToList(), page, pageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<RunnerVerificationDetails> BeginReview(Guid runnerId, CancellationToken ct)
    {
        var verification = await Find(runnerId, ct);
        verification.BeginReview();
        await verifications.Save(ct);
        await notifications.Publish(runnerId, Domain.Communications.NotificationType.System,
            "Verification under review",
            "Your runner verification is now being reviewed.", null, ct);
        return Map(verification);
    }

    public async Task<RunnerVerificationDetails> Approve(Guid runnerId, CancellationToken ct)
    {
        var verification = await Find(runnerId, ct);
        var runner = await runners.Find(runnerId, ct) ?? throw new KeyNotFoundException("Runner not found.");
        verification.Approve(clock.UtcNow);
        runner.Approve();
        await verifications.Save(ct);
        await notifications.Publish(runnerId, Domain.Communications.NotificationType.System,
            "Runner verification approved",
            "Your verification is complete. Go online to receive nearby errand offers.", null, ct);
        return Map(verification);
    }

    public async Task<RunnerVerificationDetails> Reject(Guid runnerId, string? reason, CancellationToken ct)
    {
        var verification = await Find(runnerId, ct);
        var runner = await runners.Find(runnerId, ct) ?? throw new KeyNotFoundException("Runner not found.");
        verification.Reject(reason ?? string.Empty, clock.UtcNow);
        runner.Reject();
        await verifications.Save(ct);
        await notifications.Publish(runnerId, Domain.Communications.NotificationType.System,
            "Runner verification needs attention",
            $"Your verification was not approved. {verification.RejectionReason}", null, ct);
        return Map(verification);
    }

    private async Task<RunnerVerification> GetOrCreate(CancellationToken ct)
    {
        _ = await Runner(ct);
        var value = await verifications.Find(current.UserId, ct);
        if (value is not null) return value;
        value = new RunnerVerification(current.UserId);
        verifications.Add(value);
        await verifications.Save(ct);
        return value;
    }

    private async Task<RunnerProfile> Runner(CancellationToken ct) =>
        await runners.Find(current.UserId, ct) ?? throw new KeyNotFoundException("Runner profile not found.");

    private async Task<RunnerVerification> Find(Guid runnerId, CancellationToken ct) =>
        await verifications.Find(runnerId, ct) ?? throw new KeyNotFoundException("Runner verification not found.");

    private static RunnerVerificationDetails Map(RunnerVerification value)
    {
        var progress = value.Status == VerificationStatus.Approved ? 100 :
            (string.IsNullOrWhiteSpace(value.IdentityDocumentKey) ? 0 : 40) +
            (value.VehicleType is null ? 0 : 40) +
            (value.Status is VerificationStatus.Submitted or VerificationStatus.UnderReview ? 20 : 0);
        return new(value.RunnerId, value.Status, value.GovernmentIdType,
            !string.IsNullOrWhiteSpace(value.IdentityDocumentKey), value.VehicleType,
            value.VehiclePlateNumber, value.VehicleMakeModel,
            !string.IsNullOrWhiteSpace(value.OwnershipDocumentKey), value.SubmittedAt,
            value.ReviewedAt, value.RejectionReason, progress);
    }
}

public sealed class RunnerOfferService(
    IErrandRepository errands,
    IRunnerRepository runners,
    RunnerCompensationPolicy compensation,
    INotificationPublisher notifications,
    ICurrentUser current,
    IClock clock)
{
    public async Task<PagedRunnerOffers> Nearby(decimal latitude, decimal longitude,
        decimal radiusKm, ErrandCategory? category, int page, int pageSize, CancellationToken ct)
    {
        _ = new GeoPoint(latitude, longitude);
        if (radiusKm is <= 0 or > 50) throw new ArgumentException("Radius must be between 0 and 50 kilometres.");
        await EnsureAvailable(ct);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var declined = await runners.DeclinedOfferIds(current.UserId, ct);
        var latitudeDelta = radiusKm / 111.32m;
        var longitudeScale = Math.Max(0.01m,
            (decimal)Math.Cos((double)latitude * Math.PI / 180d));
        var longitudeDelta = radiusKm / (111.32m * longitudeScale);
        var candidates = await errands.ListAvailableOffers(latitude - latitudeDelta,
            latitude + latitudeDelta, longitude - longitudeDelta,
            longitude + longitudeDelta, 500, ct);
        var matches = candidates.Where(e => category is null || e.Category == category)
            .Where(e => !declined.Contains(e.Id))
            .Select(e => Map(e, Distance(latitude, longitude, e.Stops[0].Location.Latitude, e.Stops[0].Location.Longitude)))
            .Where(x => x.DistanceKm <= radiusKm).OrderBy(x => x.DistanceKm).ThenBy(x => x.ScheduledFor).ToList();
        return new(matches.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page,
            pageSize, matches.Count, matches.Count == 0 ? 0 : (int)Math.Ceiling(matches.Count / (double)pageSize));
    }

    public async Task<RunnerOfferDetails> Get(Guid id, decimal latitude, decimal longitude, CancellationToken ct)
    {
        _ = new GeoPoint(latitude, longitude);
        await EnsureAvailable(ct);
        var e = await errands.Find(id, ct) ?? throw new KeyNotFoundException("Errand offer not found.");
        EnsureOffer(e);
        var summary = Map(e, Distance(latitude, longitude, e.Stops[0].Location.Latitude, e.Stops[0].Location.Longitude));
        return new(summary, e.PreferredProvider, e.SpecialInstructions,
            e.Stops.Select(MapStop).ToList(), e.Items.Select(i => new ErrandItemDetails(i.Id, i.Name, i.Quantity, i.Unit, i.EstimatedUnitPrice)).ToList());
    }

    public async Task<ErrandSummary> Accept(Guid id, CancellationToken ct)
    {
        var runner = await EnsureAvailable(ct);
        var e = await errands.Find(id, ct) ?? throw new KeyNotFoundException("Errand offer not found.");
        EnsureOffer(e);
        e.BeginMatching();
        e.AssignRunner(current.UserId);
        e.Accept(current.UserId);
        runner.Assign();
        try { await errands.Save(ct); }
        catch (ConcurrencyConflictException) { throw new DomainException("This errand was accepted by another runner."); }
        await notifications.Publish(e.CustomerId, Domain.Communications.NotificationType.ErrandUpdate,
            "Runner accepted your errand", e.Title, e.Id, ct);
        await notifications.Publish(current.UserId, Domain.Communications.NotificationType.NewAssignment,
            "Errand accepted", $"You accepted {e.Title}. Open the job to review the route.", e.Id, ct);
        return new(e.Id, e.Title, e.Category, e.Status, e.Stops.Count, e.RunnerId,
            new(e.TotalEstimate, e.Currency), e.CreatedAt);
    }

    public async Task Decline(Guid id, CancellationToken ct)
    {
        await EnsureAvailable(ct);
        var e = await errands.Find(id, ct) ?? throw new KeyNotFoundException("Errand offer not found.");
        EnsureOffer(e);
        if (!await runners.HasDeclinedOffer(current.UserId, id, ct))
        {
            runners.AddDeclinedOffer(new RunnerDeclinedOffer(current.UserId, id, clock.UtcNow));
            await runners.Save(ct);
        }
    }

    private async Task<RunnerProfile> EnsureAvailable(CancellationToken ct)
    {
        var runner = await runners.Find(current.UserId, ct) ?? throw new KeyNotFoundException("Runner profile not found.");
        if (runner.Status != RunnerStatus.Available)
            throw new DomainException("Only verified runners who are online can view or accept nearby errands.");
        return runner;
    }

    private static void EnsureOffer(Errand e)
    {
        if (e.Status != ErrandStatus.PaymentConfirmed || e.RunnerId is not null || e.Stops.Count == 0)
            throw new DomainException("This errand is no longer available.");
    }

    private RunnerOfferSummary Map(Errand e, decimal distance)
    {
        EnsureOffer(e);
        var earning = compensation.Calculate(e);
        var minutes = (int)Math.Ceiling(distance / 20m * 60m + e.Stops.Count * 10m);
        return new(e.Id, e.Title, e.Category, e.Stops.Count, decimal.Round(distance, 1),
            Math.Max(5, minutes), new(earning.Amount, earning.Currency), e.ScheduledFor, MapStop(e.Stops[0]));
    }

    private static RunnerOfferStop MapStop(ErrandStop stop) => new(stop.Id, stop.Sequence,
        stop.Type, stop.Address, stop.Location.Latitude, stop.Location.Longitude, stop.Instructions);

    internal static decimal Distance(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        const double radius = 6371d;
        static double Radians(decimal value) => (double)value * Math.PI / 180d;
        var dLat = Radians(lat2 - lat1); var dLon = Radians(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Radians(lat1)) *
            Math.Cos(Radians(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return (decimal)(radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)));
    }
}

public sealed class CustomerRunnerDiscoveryService(
    IRunnerRepository runners,
    ICurrentUser current,
    IClock clock)
{
    private static readonly TimeSpan LocationFreshness = TimeSpan.FromMinutes(5);

    public async Task<PagedNearbyRunners> Nearby(decimal latitude, decimal longitude,
        decimal radiusKm, int page, int pageSize, CancellationToken ct)
    {
        if (!current.IsInRole("Customer")) throw new UnauthorizedAccessException();
        _ = new GeoPoint(latitude, longitude);
        if (radiusKm is <= 0 or > 50)
            throw new ArgumentException("Radius must be between 0 and 50 kilometres.");
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var latitudeDelta = radiusKm / 111.32m;
        var longitudeScale = Math.Max(0.01m,
            (decimal)Math.Cos((double)latitude * Math.PI / 180d));
        var longitudeDelta = radiusKm / (111.32m * longitudeScale);
        var candidates = await runners.SearchNearby(latitude - latitudeDelta,
            latitude + latitudeDelta, longitude - longitudeDelta, longitude + longitudeDelta,
            clock.UtcNow - LocationFreshness, 1000, ct);
        var matches = candidates.Select(value => new
            {
                Value = value,
                Distance = RunnerOfferService.Distance(latitude, longitude,
                    value.Latitude, value.Longitude)
            })
            .Where(value => value.Distance <= radiusKm)
            .OrderBy(value => value.Distance)
            .ThenByDescending(value => value.Value.Rating)
            .ThenByDescending(value => value.Value.CompletedErrands)
            .ToList();
        var items = matches.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(value => new NearbyRunnerSummary(value.Value.RunnerId,
                value.Value.DisplayName, decimal.Round(value.Distance, 1),
                value.Value.Rating, value.Value.CompletedErrands,
                value.Value.LocationUpdatedAt, value.Value.HasProfilePicture
                    ? $"/api/v1/users/{value.Value.RunnerId:D}/profile-picture"
                    : null))
            .ToList();
        return new(items, page, pageSize, matches.Count,
            matches.Count == 0 ? 0 : (int)Math.Ceiling(matches.Count / (double)pageSize));
    }
}
