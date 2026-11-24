using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Errands;
namespace ErrandRuns.Domain.Runners;

public enum RunnerStatus { Applicant, PendingVerification, Verified, Available, Unavailable, Busy, Suspended, Rejected, Deactivated }
public sealed class RunnerProfile
{
    private RunnerProfile() { }
    public RunnerProfile(Guid userId) { UserId = userId; Status = RunnerStatus.Applicant; }
    public Guid UserId { get; private set; }
    public RunnerStatus Status { get; private set; }
    public decimal Rating { get; private set; }
    public int CompletedErrands { get; private set; }
    public decimal? CurrentLatitude { get; private set; }
    public decimal? CurrentLongitude { get; private set; }
    public DateTimeOffset? LocationUpdatedAt { get; private set; }
    public void SubmitVerification() { if (Status != RunnerStatus.Applicant) throw new DomainException("Invalid runner state."); Status = RunnerStatus.PendingVerification; }
    public void Approve() { if (Status != RunnerStatus.PendingVerification) throw new DomainException("Invalid runner state."); Status = RunnerStatus.Verified; }
    public void Reject() { if (Status != RunnerStatus.PendingVerification) throw new DomainException("Invalid runner state."); Status = RunnerStatus.Rejected; }
    public void SetAvailable(bool available) { if (Status is not (RunnerStatus.Verified or RunnerStatus.Available or RunnerStatus.Unavailable)) throw new DomainException("Runner is not eligible."); Status = available ? RunnerStatus.Available : RunnerStatus.Unavailable; }
    public void UpdateLocation(decimal latitude, decimal longitude, DateTimeOffset now)
    {
        if (Status is RunnerStatus.Applicant or RunnerStatus.PendingVerification or RunnerStatus.Rejected or RunnerStatus.Suspended or RunnerStatus.Deactivated)
            throw new DomainException("Only verified runners can publish their location.");
        _ = new GeoPoint(latitude, longitude);
        CurrentLatitude = latitude;
        CurrentLongitude = longitude;
        LocationUpdatedAt = now;
    }
    public void Assign() { if (Status != RunnerStatus.Available) throw new DomainException("Runner is unavailable."); Status = RunnerStatus.Busy; }
    public void ReleaseAssignment()
    {
        if (Status != RunnerStatus.Busy) throw new DomainException("Runner has no active assignment.");
        Status = RunnerStatus.Available;
    }
    public void CompleteErrand()
    {
        if (Status != RunnerStatus.Busy) throw new DomainException("Runner has no active assignment.");
        CompletedErrands++;
        Status = RunnerStatus.Available;
    }
    public void UpdateRating(decimal rating) => Rating = Math.Round(Math.Clamp(rating,0m,5m),2);
}

public sealed class RunnerDeclinedOffer
{
    private RunnerDeclinedOffer() { }
    public RunnerDeclinedOffer(Guid runnerId, Guid errandId, DateTimeOffset declinedAt)
    {
        RunnerId = runnerId;
        ErrandId = errandId;
        DeclinedAt = declinedAt;
    }
    public Guid RunnerId { get; private set; }
    public Guid ErrandId { get; private set; }
    public DateTimeOffset DeclinedAt { get; private set; }
}
