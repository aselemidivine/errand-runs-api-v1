using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Reviews;

namespace ErrandRuns.UnitTests;

public sealed class ReviewTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Rating_must_be_between_one_and_five(int rating)
    {
        Assert.Throws<DomainException>(()=>new ErrandReview(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),rating,null,DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Review_normalizes_comment_and_keeps_transaction_participants()
    {
        var errandId=Guid.NewGuid();var reviewerId=Guid.NewGuid();var revieweeId=Guid.NewGuid();
        var review=new ErrandReview(Guid.NewGuid(),errandId,reviewerId,revieweeId,5,"  Excellent service  ",DateTimeOffset.UtcNow);
        Assert.Equal(errandId,review.ErrandId);Assert.Equal(reviewerId,review.ReviewerId);Assert.Equal(revieweeId,review.RevieweeId);Assert.Equal("Excellent service",review.Comment);
    }

    [Fact]
    public void User_cannot_review_themselves()
    {
        var userId=Guid.NewGuid();
        Assert.Throws<DomainException>(()=>new ErrandReview(Guid.NewGuid(),Guid.NewGuid(),userId,userId,5,null,DateTimeOffset.UtcNow));
    }
}
