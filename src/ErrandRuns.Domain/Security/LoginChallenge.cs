namespace ErrandRuns.Domain.Security;
public sealed class LoginChallenge
{
    private LoginChallenge(){Method=CodeHash=string.Empty;}
    public LoginChallenge(Guid id,Guid userId,string method,string? codeHash,DateTimeOffset createdAt,DateTimeOffset expiresAt){Id=id;UserId=userId;Method=method;CodeHash=codeHash??string.Empty;CreatedAt=createdAt;ExpiresAt=expiresAt;}
    public Guid Id{get;private set;} public Guid UserId{get;private set;} public string Method{get;private set;} public string CodeHash{get;private set;} public int FailedAttempts{get;private set;} public DateTimeOffset CreatedAt{get;private set;} public DateTimeOffset ExpiresAt{get;private set;} public DateTimeOffset? UsedAt{get;private set;}
    public bool CanUse(DateTimeOffset now)=>UsedAt is null&&ExpiresAt>now&&FailedAttempts<5;
    public void Fail()=>FailedAttempts++;
    public void Use(DateTimeOffset now)=>UsedAt=now;
}
