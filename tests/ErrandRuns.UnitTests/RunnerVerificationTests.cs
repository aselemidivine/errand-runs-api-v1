using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Runners;

namespace ErrandRuns.UnitTests;

public sealed class RunnerVerificationTests
{
    [Fact]
    public void Submit_requires_identity_and_vehicle()
    {
        var verification = new RunnerVerification(Guid.NewGuid());
        Assert.Throws<DomainException>(() => verification.Submit(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Motorbike_requires_registration_details()
    {
        var verification = new RunnerVerification(Guid.NewGuid());
        Assert.Throws<DomainException>(() => verification.SetVehicle(
            RunnerVehicleType.Motorbike, null, null, null));
    }

    [Fact]
    public void Complete_verification_can_be_submitted_and_approved()
    {
        var verification = new RunnerVerification(Guid.NewGuid());
        verification.SetIdentity(GovernmentIdType.DriversLicense, "private/id.png");
        verification.SetVehicle(RunnerVehicleType.Motorbike, "LAG-123-XY", "Honda CG125", "private/vehicle.png");
        verification.Submit(DateTimeOffset.UtcNow);
        verification.Approve(DateTimeOffset.UtcNow);
        Assert.Equal(VerificationStatus.Approved, verification.Status);
    }
}
