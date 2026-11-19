namespace ErrandRuns.Domain.Runners;

public enum VerificationStatus { Draft, Submitted, UnderReview, Approved, Rejected }
public enum GovernmentIdType { NinSlip, NationalIdCard, DriversLicense, InternationalPassport }
public enum RunnerVehicleType { Motorbike, Car, Bicycle, Walking }

public sealed class RunnerVerification
{
    private RunnerVerification() { IdentityDocumentKey = string.Empty; }
    public RunnerVerification(Guid runnerId) { RunnerId = runnerId; IdentityDocumentKey = string.Empty; }

    public Guid RunnerId { get; private set; }
    public VerificationStatus Status { get; private set; }
    public GovernmentIdType? GovernmentIdType { get; private set; }
    public string IdentityDocumentKey { get; private set; }
    public RunnerVehicleType? VehicleType { get; private set; }
    public string? VehiclePlateNumber { get; private set; }
    public string? VehicleMakeModel { get; private set; }
    public string? OwnershipDocumentKey { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void SetIdentity(GovernmentIdType type, string documentKey)
    {
        EnsureEditable();
        GovernmentIdType = type;
        IdentityDocumentKey = Required(documentKey, "Identity document is required.");
    }

    public void SetVehicle(RunnerVehicleType type, string? plate, string? makeModel, string? ownershipDocumentKey)
    {
        EnsureEditable();
        if (type is RunnerVehicleType.Motorbike or RunnerVehicleType.Car)
        {
            VehiclePlateNumber = Required(plate, "Vehicle plate number is required.").ToUpperInvariant();
            VehicleMakeModel = Required(makeModel, "Vehicle make and model are required.");
            OwnershipDocumentKey = Required(ownershipDocumentKey, "Proof of ownership or registration is required.");
        }
        else
        {
            VehiclePlateNumber = null;
            VehicleMakeModel = null;
            OwnershipDocumentKey = null;
        }
        VehicleType = type;
    }

    public void Submit(DateTimeOffset now)
    {
        EnsureEditable();
        if (GovernmentIdType is null || string.IsNullOrWhiteSpace(IdentityDocumentKey))
            throw new Common.DomainException("Complete identity verification first.");
        if (VehicleType is null) throw new Common.DomainException("Complete vehicle verification first.");
        Status = VerificationStatus.Submitted;
        SubmittedAt = now;
        RejectionReason = null;
    }

    public void BeginReview() { if (Status != VerificationStatus.Submitted) throw new Common.DomainException("Verification is not awaiting review."); Status = VerificationStatus.UnderReview; }
    public void Approve(DateTimeOffset now) { if (Status is not (VerificationStatus.Submitted or VerificationStatus.UnderReview)) throw new Common.DomainException("Verification cannot be approved."); Status = VerificationStatus.Approved; ReviewedAt = now; RejectionReason = null; }
    public void Reject(string reason, DateTimeOffset now) { if (Status is not (VerificationStatus.Submitted or VerificationStatus.UnderReview)) throw new Common.DomainException("Verification cannot be rejected."); RejectionReason = Required(reason, "Rejection reason is required."); Status = VerificationStatus.Rejected; ReviewedAt = now; }
    private void EnsureEditable() { if (Status is VerificationStatus.Submitted or VerificationStatus.UnderReview or VerificationStatus.Approved) throw new Common.DomainException("Submitted verification cannot be edited."); if (Status == VerificationStatus.Rejected) Status = VerificationStatus.Draft; }
    private static string Required(string? value, string message) => string.IsNullOrWhiteSpace(value) ? throw new Common.DomainException(message) : value.Trim();
}
