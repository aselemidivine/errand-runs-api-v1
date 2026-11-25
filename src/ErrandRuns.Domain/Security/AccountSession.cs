namespace ErrandRuns.Domain.Security;

public sealed class AccountSession
{
    private AccountSession(){RefreshTokenHash=DeviceName=Platform=UserAgent=IpAddress=string.Empty;}
    public AccountSession(Guid id,Guid userId,string refreshTokenHash,string deviceName,string platform,string userAgent,string ipAddress,DateTimeOffset createdAt,DateTimeOffset expiresAt)
    {Id=id;UserId=userId;RefreshTokenHash=refreshTokenHash;DeviceName=Clean(deviceName,120,"Unknown device");Platform=Clean(platform,40,"Unknown");UserAgent=Clean(userAgent,500,"Unknown");IpAddress=Clean(ipAddress,64,"Unknown");CreatedAt=LastSeenAt=createdAt;ExpiresAt=expiresAt;}
    public Guid Id{get;private set;} public Guid UserId{get;private set;} public string RefreshTokenHash{get;private set;}
    public string DeviceName{get;private set;} public string Platform{get;private set;} public string UserAgent{get;private set;} public string IpAddress{get;private set;}
    public DateTimeOffset CreatedAt{get;private set;} public DateTimeOffset LastSeenAt{get;private set;} public DateTimeOffset ExpiresAt{get;private set;} public DateTimeOffset? RevokedAt{get;private set;}
    public bool IsActive(DateTimeOffset now)=>RevokedAt is null&&ExpiresAt>now;
    public void Rotate(string hash,DateTimeOffset now,DateTimeOffset expiresAt){RefreshTokenHash=hash;LastSeenAt=now;ExpiresAt=expiresAt;}
    public void Touch(DateTimeOffset now)=>LastSeenAt=now;
    public void Revoke(DateTimeOffset now)=>RevokedAt??=now;
    private static string Clean(string? value,int max,string fallback){var result=string.IsNullOrWhiteSpace(value)?fallback:value.Trim();return result.Length<=max?result:result[..max];}
}
