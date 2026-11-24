using ErrandRuns.Domain.Common;

namespace ErrandRuns.Domain.Reviews;

public sealed class ErrandReview
{
    private ErrandReview() { Comment = string.Empty; }
    public ErrandReview(Guid id,Guid errandId,Guid reviewerId,Guid revieweeId,int rating,string? comment,DateTimeOffset createdAt)
    {
        if(reviewerId==revieweeId)throw new DomainException("You cannot review yourself.");
        if(rating is <1 or >5)throw new DomainException("Rating must be between 1 and 5.");
        if(comment?.Trim().Length>1000)throw new DomainException("Review cannot exceed 1000 characters.");
        Id=id;ErrandId=errandId;ReviewerId=reviewerId;RevieweeId=revieweeId;Rating=rating;Comment=comment?.Trim()??string.Empty;CreatedAt=createdAt;
    }
    public Guid Id{get;private set;}
    public Guid ErrandId{get;private set;}
    public Guid ReviewerId{get;private set;}
    public Guid RevieweeId{get;private set;}
    public int Rating{get;private set;}
    public string Comment{get;private set;}
    public DateTimeOffset CreatedAt{get;private set;}
}
