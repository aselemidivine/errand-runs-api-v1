using ErrandRuns.Application;
using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Errands;
namespace ErrandRuns.UnitTests;
public sealed class ErrandTests
{
    [Fact] public void Single_location_errand_does_not_require_a_delivery_stop() { var e=New();e.AddStop(Stop(1,StopType.Pickup));e.RequestEstimate();Assert.Equal(ErrandStatus.PendingEstimate,e.Status); }
    [Fact] public void Requires_at_least_one_stop() { var e=New();Assert.Throws<DomainException>(e.RequestEstimate); }
    [Fact] public void Single_stop_uses_the_base_service_fee() { var fee=new PricingService().Estimate(1,0);Assert.Equal(2500m,fee.Amount); }
    [Fact] public void Enforces_stop_order() { var runner=Guid.NewGuid();var e=Ready(runner);e.Accept(runner);e.StartJourney(runner);Assert.Throws<DomainException>(()=>e.StartStop(runner,e.Stops[1].Id)); }
    [Fact] public void Assigned_runner_can_complete_route() { var runner=Guid.NewGuid();var e=Ready(runner);e.Accept(runner);e.StartJourney(runner);foreach(var stop in e.Stops){e.StartStop(runner,stop.Id);e.CompleteStop(runner,stop.Id,DateTimeOffset.UtcNow);}Assert.Equal(ErrandStatus.AwaitingConfirmation,e.Status); }
    [Fact] public void Stores_items_and_server_estimate() { var e=New();e.AddStop(Stop(1,StopType.Shopping));e.AddStop(Stop(2,StopType.Delivery));e.AddItem(new(Guid.NewGuid(),"Brown eggs",2,"crate",3500));e.RequestEstimate();e.SetEstimate(new Money(2500),new Money(7000));Assert.Equal(ErrandStatus.PendingPayment,e.Status);Assert.Equal(9500,e.TotalEstimate);Assert.Single(e.Items); }
    [Fact] public void Customer_can_cancel_an_open_errand() { var e=New();e.Cancel(e.CustomerId);Assert.Equal(ErrandStatus.Cancelled,e.Status); }
    [Fact] public void Customer_confirms_completed_route() { var runner=Guid.NewGuid();var e=Ready(runner);e.Accept(runner);e.StartJourney(runner);foreach(var stop in e.Stops){e.StartStop(runner,stop.Id);e.CompleteStop(runner,stop.Id,DateTimeOffset.UtcNow);}e.ConfirmCompletion(e.CustomerId);Assert.Equal(ErrandStatus.Completed,e.Status); }
    private static Errand New()=>new(Guid.NewGuid(),Guid.NewGuid(),"Collect parcel",ErrandCategory.DocumentCollection);
    private static ErrandStop Stop(int n,StopType type)=>new(Guid.NewGuid(),n,type,$"Address {n}",new(6.5m,3.3m),null);
    private static Errand Ready(Guid runner){var e=New();e.AddStop(Stop(1,StopType.Pickup));e.AddStop(Stop(2,StopType.Delivery));e.RequestEstimate();e.SetEstimate();e.ConfirmPayment();e.BeginMatching();e.AssignRunner(runner);return e;}
}
