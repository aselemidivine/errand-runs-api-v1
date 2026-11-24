using ErrandRuns.Domain.Communications;
using ErrandRuns.Domain.Errands;
using ErrandRuns.Domain.Reviews;

namespace ErrandRuns.Application;

public interface IReviewRepository
{
    Task<ErrandReview?> Find(Guid errandId,Guid reviewerId,CancellationToken ct);
    Task<IReadOnlyList<ReviewListItem>> ListForUser(Guid userId,int skip,int take,CancellationToken ct);
    Task<int> CountForUser(Guid userId,CancellationToken ct);
    Task<RatingSummary> RatingForUser(Guid userId,CancellationToken ct);
    void Add(ErrandReview review);
    Task Save(CancellationToken ct);
}
public sealed record SubmitReview(int Rating,string? Comment=null);
public sealed record RatingSummary(decimal AverageRating,int ReviewCount);
public sealed record ReviewListItem(Guid Id,Guid ErrandId,Guid ReviewerId,string ReviewerDisplayName,int Rating,string? Comment,DateTimeOffset CreatedAt);
public sealed record ReviewDetails(Guid Id,Guid ErrandId,Guid ReviewerId,Guid RevieweeId,int Rating,string? Comment,DateTimeOffset CreatedAt);
public sealed record PagedReviews(IReadOnlyList<ReviewListItem> Items,int Offset,int Limit,int TotalCount,bool HasMore,RatingSummary Rating);

public sealed class ReviewService(IReviewRepository reviews,IErrandRepository errands,IRunnerRepository runners,INotificationPublisher notifications,ICurrentUser current,IClock clock)
{
    public async Task<ReviewDetails> Submit(Guid errandId,SubmitReview request,CancellationToken ct)
    {
        var errand=await errands.Find(errandId,ct)??throw new KeyNotFoundException("Errand not found.");
        if(errand.Status!=ErrandStatus.Completed)throw new Domain.Common.DomainException("Reviews are available only after a completed errand.");
        if(errand.RunnerId is not Guid runnerId)throw new Domain.Common.DomainException("This errand has no runner to review.");
        var reviewee=current.UserId==errand.CustomerId?runnerId:current.UserId==runnerId?errand.CustomerId:throw new UnauthorizedAccessException();
        if(await reviews.Find(errandId,current.UserId,ct) is not null)throw new Domain.Common.DomainException("You have already reviewed this errand.");
        var value=new ErrandReview(Guid.NewGuid(),errandId,current.UserId,reviewee,request.Rating,request.Comment,clock.UtcNow);
        reviews.Add(value);await reviews.Save(ct);
        if(reviewee==runnerId&&await runners.Find(runnerId,ct) is{} runner){var rating=await reviews.RatingForUser(runnerId,ct);runner.UpdateRating(rating.AverageRating);await runners.Save(ct);}
        await notifications.Publish(reviewee,NotificationType.System,"New rating",$"You received a {request.Rating}-star rating.",errandId,ct);
        return Map(value);
    }
    public async Task<PagedReviews> List(Guid userId,int offset,int limit,CancellationToken ct)
    {offset=Math.Max(0,offset);limit=Math.Clamp(limit,1,100);var total=await reviews.CountForUser(userId,ct);return new(await reviews.ListForUser(userId,offset,limit,ct),offset,limit,total,offset+limit<total,await reviews.RatingForUser(userId,ct));}
    public Task<RatingSummary> Rating(Guid userId,CancellationToken ct)=>reviews.RatingForUser(userId,ct);
    private static ReviewDetails Map(ErrandReview x)=>new(x.Id,x.ErrandId,x.ReviewerId,x.RevieweeId,x.Rating,string.IsNullOrEmpty(x.Comment)?null:x.Comment,x.CreatedAt);
}
