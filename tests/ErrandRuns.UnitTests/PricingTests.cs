using ErrandRuns.Application;
namespace ErrandRuns.UnitTests;
public sealed class PricingTests { [Fact] public void Applies_stops_and_membership_discount(){var result=new PricingService().Estimate(4,10);Assert.Equal(3600m,result.Amount);Assert.Equal("NGN",result.Currency);} }
