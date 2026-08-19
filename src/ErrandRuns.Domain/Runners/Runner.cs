using ErrandRuns.Domain.Common;
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
    public void SubmitVerification() { if (Status != RunnerStatus.Applicant) throw new DomainException("Invalid runner state."); Status = RunnerStatus.PendingVerification; }
    public void Approve() { if (Status != RunnerStatus.PendingVerification) throw new DomainException("Invalid runner state."); Status = RunnerStatus.Verified; }
    public void SetAvailable(bool available) { if (Status is not (RunnerStatus.Verified or RunnerStatus.Available or RunnerStatus.Unavailable)) throw new DomainException("Runner is not eligible."); Status = available ? RunnerStatus.Available : RunnerStatus.Unavailable; }
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
}
