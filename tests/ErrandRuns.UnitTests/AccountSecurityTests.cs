using ErrandRuns.Domain.Security;

namespace ErrandRuns.UnitTests;

public sealed class AccountSecurityTests
{
    [Fact]
    public void Revoked_session_is_immediately_inactive()
    {
        var now=DateTimeOffset.UtcNow;var session=new AccountSession(Guid.NewGuid(),Guid.NewGuid(),new string('A',64),"iPhone","ios","app","127.0.0.1",now,now.AddDays(30));
        Assert.True(session.IsActive(now));session.Revoke(now);Assert.False(session.IsActive(now));
    }

    [Fact]
    public void Login_challenge_locks_after_five_failures()
    {
        var now=DateTimeOffset.UtcNow;var challenge=new LoginChallenge(Guid.NewGuid(),Guid.NewGuid(),"sms","hash",now,now.AddMinutes(5));
        for(var i=0;i<5;i++)challenge.Fail();Assert.False(challenge.CanUse(now));
    }
}
