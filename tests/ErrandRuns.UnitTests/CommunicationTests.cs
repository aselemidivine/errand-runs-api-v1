using ErrandRuns.Domain.Communications;
using ErrandRuns.Domain.Common;

namespace ErrandRuns.UnitTests;

public sealed class CommunicationTests
{
    [Fact]
    public void Only_participants_can_message()
    {
        var customer=Guid.NewGuid();var runner=Guid.NewGuid();var conversation=new Conversation(Guid.NewGuid(),Guid.NewGuid(),customer,runner,DateTimeOffset.UtcNow);
        conversation.Send(Guid.NewGuid(),customer,"I am at the gate",DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(()=>conversation.Send(Guid.NewGuid(),Guid.NewGuid(),"Intrusion",DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Callee_can_answer_and_participant_can_end_call()
    {
        var caller=Guid.NewGuid();var callee=Guid.NewGuid();var call=new VoiceCallSession(Guid.NewGuid(),Guid.NewGuid(),caller,callee,DateTimeOffset.UtcNow);
        call.Answer(callee,DateTimeOffset.UtcNow);call.End(caller,"Completed",DateTimeOffset.UtcNow);
        Assert.Equal(VoiceCallStatus.Ended,call.Status);
    }

    [Fact]
    public void Notification_read_is_idempotent()
    {
        var notification=new UserNotification(Guid.NewGuid(),Guid.NewGuid(),NotificationType.Message,"New message","Your runner sent a message.",null,DateTimeOffset.UtcNow);
        var readAt=DateTimeOffset.UtcNow;notification.MarkRead(readAt);notification.MarkRead(readAt.AddMinutes(1));
        Assert.Equal(readAt,notification.ReadAt);
    }
}
