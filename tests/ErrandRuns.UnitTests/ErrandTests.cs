using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Errands;
namespace ErrandRuns.UnitTests;
public sealed class ErrandTests
{
    [Fact] public void Requires_delivery_and_two_stops() { var e=New();e.AddStop(Stop(1,StopType.Pickup));Assert.Throws<DomainException>(e.RequestEstimate); }
    [Fact] public void Enforces_stop_order() { var runner=Guid.NewGuid();var e=Ready(runner);e.Accept(runner);e.StartJourney(runner);Assert.Throws<DomainException>(()=>e.StartStop(runner,e.Stops[1].Id)); }
    [Fact] public void Assigned_runner_can_complete_route() { var runner=Guid.NewGuid();var e=Ready(runner);e.Accept(runner);e.StartJourney(runner);foreach(var stop in e.Stops){e.StartStop(runner,stop.Id);e.CompleteStop(runner,stop.Id,DateTimeOffset.UtcNow);}Assert.Equal(ErrandStatus.AwaitingConfirmation,e.Status); }
    private static Errand New()=>new(Guid.NewGuid(),Guid.NewGuid(),"Collect parcel",ErrandCategory.DocumentCollection);
    private static ErrandStop Stop(int n,StopType type)=>new(Guid.NewGuid(),n,type,$"Address {n}",new(6.5m,3.3m),null);
    private static Errand Ready(Guid runner){var e=New();e.AddStop(Stop(1,StopType.Pickup));e.AddStop(Stop(2,StopType.Delivery));e.RequestEstimate();e.SetEstimate();e.ConfirmPayment();e.BeginMatching();e.AssignRunner(runner);return e;}
}
