using ErrandRuns.Domain.Common;

namespace ErrandRuns.Domain.Communications;

public enum NotificationType { ErrandUpdate, NewAssignment, Message, Call, Payment, System }
public enum VoiceCallStatus { Ringing, Active, Declined, Ended, Missed }

public sealed class UserNotification
{
    private UserNotification() { Title = Body = string.Empty; }
    public UserNotification(Guid id, Guid recipientId, NotificationType type, string title,
        string body, Guid? errandId, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 160) throw new DomainException("A valid notification title is required.");
        if (string.IsNullOrWhiteSpace(body) || body.Trim().Length > 1000) throw new DomainException("A valid notification body is required.");
        Id=id;RecipientId=recipientId;Type=type;Title=title.Trim();Body=body.Trim();ErrandId=errandId;CreatedAt=createdAt;
    }
    public Guid Id { get; private set; }
    public Guid RecipientId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Title { get; private set; }
    public string Body { get; private set; }
    public Guid? ErrandId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}

public sealed class Conversation
{
    private readonly List<ChatMessage> _messages=[];
    private Conversation() { }
    public Conversation(Guid id,Guid errandId,Guid customerId,Guid runnerId,DateTimeOffset createdAt)
    {Id=id;ErrandId=errandId;CustomerId=customerId;RunnerId=runnerId;CreatedAt=createdAt;}
    public Guid Id { get; private set; }
    public Guid ErrandId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid RunnerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<ChatMessage> Messages=>_messages.OrderBy(x=>x.SentAt).ToList();
    public bool Includes(Guid userId)=>userId==CustomerId||userId==RunnerId;
    public ChatMessage Send(Guid id,Guid senderId,string body,DateTimeOffset now)
    {if(!Includes(senderId))throw new DomainException("Only errand participants can send messages.");if(string.IsNullOrWhiteSpace(body)||body.Trim().Length>2000)throw new DomainException("Message must contain 1-2000 characters.");var message=new ChatMessage(id,senderId,body.Trim(),now);_messages.Add(message);return message;}
}

public sealed class ChatMessage
{
    private ChatMessage(){Body=string.Empty;}
    internal ChatMessage(Guid id,Guid senderId,string body,DateTimeOffset sentAt){Id=id;SenderId=senderId;Body=body;SentAt=sentAt;}
    public Guid Id { get; private set; }
    public Guid SenderId { get; private set; }
    public string Body { get; private set; }
    public DateTimeOffset SentAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public void MarkRead(Guid readerId,DateTimeOffset now){if(readerId==SenderId)throw new DomainException("A sender cannot mark their own message as read.");ReadAt??=now;}
}

public sealed class VoiceCallSession
{
    private VoiceCallSession(){EndReason=string.Empty;}
    public VoiceCallSession(Guid id,Guid conversationId,Guid callerId,Guid calleeId,DateTimeOffset createdAt)
    {if(callerId==calleeId)throw new DomainException("Caller and recipient must differ.");Id=id;ConversationId=conversationId;CallerId=callerId;CalleeId=calleeId;CreatedAt=createdAt;Status=VoiceCallStatus.Ringing;}
    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid CallerId { get; private set; }
    public Guid CalleeId { get; private set; }
    public VoiceCallStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AnsweredAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public string EndReason { get; private set; }=string.Empty;
    public bool Includes(Guid id)=>id==CallerId||id==CalleeId;
    public void Answer(Guid userId,DateTimeOffset now){if(userId!=CalleeId||Status!=VoiceCallStatus.Ringing)throw new DomainException("Call cannot be answered.");Status=VoiceCallStatus.Active;AnsweredAt=now;}
    public void Decline(Guid userId,DateTimeOffset now){if(userId!=CalleeId||Status!=VoiceCallStatus.Ringing)throw new DomainException("Call cannot be declined.");Status=VoiceCallStatus.Declined;EndedAt=now;EndReason="Declined";}
    public void End(Guid userId,string? reason,DateTimeOffset now){if(!Includes(userId)||Status is not(VoiceCallStatus.Ringing or VoiceCallStatus.Active))throw new DomainException("Call cannot be ended.");Status=Status==VoiceCallStatus.Ringing?VoiceCallStatus.Missed:VoiceCallStatus.Ended;EndedAt=now;EndReason=string.IsNullOrWhiteSpace(reason)?"Ended":reason.Trim();}
}
