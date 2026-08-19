using ErrandRuns.Domain.Communications;

namespace ErrandRuns.Application;

public interface ICommunicationRepository
{
    Task AddNotification(UserNotification value,CancellationToken ct);
    Task<IReadOnlyList<UserNotification>> ListNotifications(Guid recipientId,bool? unreadOnly,int skip,int take,CancellationToken ct);
    Task<int> CountNotifications(Guid recipientId,bool? unreadOnly,CancellationToken ct);
    Task<UserNotification?> FindNotification(Guid id,CancellationToken ct);
    Task<Conversation?> FindConversation(Guid id,CancellationToken ct);
    Task<Conversation?> FindConversationForErrand(Guid errandId,CancellationToken ct);
    Task AddConversation(Conversation value,CancellationToken ct);
    Task<IReadOnlyList<Conversation>> ListConversations(Guid userId,int skip,int take,CancellationToken ct);
    Task<int> CountConversations(Guid userId,CancellationToken ct);
    Task<VoiceCallSession?> FindCall(Guid id,CancellationToken ct);
    Task AddCall(VoiceCallSession value,CancellationToken ct);
    Task Save(CancellationToken ct);
}

public interface IRealtimeCommunications
{
    Task NotifyUser(Guid userId,string eventName,object payload,CancellationToken ct);
    Task NotifyConversation(Guid conversationId,string eventName,object payload,CancellationToken ct);
}
public interface INotificationPublisher
{
    Task Publish(Guid recipientId,NotificationType type,string title,string body,Guid? errandId,CancellationToken ct);
}

public sealed record NotificationDetails(Guid Id,NotificationType Type,string Title,string Body,Guid? ErrandId,DateTimeOffset CreatedAt,DateTimeOffset? ReadAt);
public sealed record PagedNotifications(IReadOnlyList<NotificationDetails> Items,int Page,int PageSize,int TotalCount,int UnreadCount);
public sealed record SendMessage(string Body);
public sealed record MessageDetails(Guid Id,Guid SenderId,string Body,DateTimeOffset SentAt,DateTimeOffset? ReadAt);
public sealed record ConversationSummary(Guid Id,Guid ErrandId,Guid CustomerId,Guid RunnerId,MessageDetails? LastMessage);
public sealed record ConversationDetails(Guid Id,Guid ErrandId,Guid CustomerId,Guid RunnerId,IReadOnlyList<MessageDetails> Messages);
public sealed record PagedConversations(IReadOnlyList<ConversationSummary> Items,int Page,int PageSize,int TotalCount);
public sealed record StartCall(Guid ConversationId);
public sealed record EndCall(string? Reason);
public sealed record VoiceCallDetails(Guid Id,Guid ConversationId,Guid CallerId,Guid CalleeId,VoiceCallStatus Status,DateTimeOffset CreatedAt,DateTimeOffset? AnsweredAt,DateTimeOffset? EndedAt,string? EndReason);

public sealed class NotificationService(ICommunicationRepository communications,IRealtimeCommunications realtime,ICurrentUser current,IClock clock):INotificationPublisher
{
    public async Task Publish(Guid recipientId,NotificationType type,string title,string body,Guid? errandId,CancellationToken ct)
    {var value=new UserNotification(Guid.NewGuid(),recipientId,type,title,body,errandId,clock.UtcNow);await communications.AddNotification(value,ct);await communications.Save(ct);await realtime.NotifyUser(recipientId,"notification",Map(value),ct);}
    public async Task<PagedNotifications> List(bool? unreadOnly,int page,int pageSize,CancellationToken ct)
    {page=Math.Max(1,page);pageSize=Math.Clamp(pageSize,1,100);var total=await communications.CountNotifications(current.UserId,unreadOnly,ct);var unread=await communications.CountNotifications(current.UserId,true,ct);var values=await communications.ListNotifications(current.UserId,unreadOnly,(page-1)*pageSize,pageSize,ct);return new(values.Select(Map).ToList(),page,pageSize,total,unread);}
    public async Task MarkRead(Guid id,CancellationToken ct){var value=await communications.FindNotification(id,ct)??throw new KeyNotFoundException("Notification not found.");if(value.RecipientId!=current.UserId)throw new UnauthorizedAccessException();value.MarkRead(clock.UtcNow);await communications.Save(ct);}
    private static NotificationDetails Map(UserNotification value)=>new(value.Id,value.Type,value.Title,value.Body,value.ErrandId,value.CreatedAt,value.ReadAt);
}

public sealed class MessagingService(ICommunicationRepository communications,IErrandRepository errands,IRealtimeCommunications realtime,INotificationPublisher notifications,ICurrentUser current,IClock clock)
{
    public async Task<ConversationDetails> GetOrCreateForErrand(Guid errandId,CancellationToken ct)
    {var errand=await errands.Find(errandId,ct)??throw new KeyNotFoundException("Errand not found.");if(errand.RunnerId is not Guid runnerId)throw new Domain.Common.DomainException("A runner must be assigned before messaging starts.");if(current.UserId!=errand.CustomerId&&current.UserId!=runnerId)throw new UnauthorizedAccessException();var value=await communications.FindConversationForErrand(errandId,ct);if(value is null){value=new(Guid.NewGuid(),errandId,errand.CustomerId,runnerId,clock.UtcNow);await communications.AddConversation(value,ct);await communications.Save(ct);}return Map(value);}
    public async Task<PagedConversations> List(int page,int pageSize,CancellationToken ct)
    {page=Math.Max(1,page);pageSize=Math.Clamp(pageSize,1,100);var total=await communications.CountConversations(current.UserId,ct);var values=await communications.ListConversations(current.UserId,(page-1)*pageSize,pageSize,ct);return new(values.Select(Summary).ToList(),page,pageSize,total);}
    public async Task<ConversationDetails> Get(Guid id,CancellationToken ct)=>Map(await Owned(id,ct));
    public async Task<MessageDetails> Send(Guid id,SendMessage request,CancellationToken ct)
    {var conversation=await Owned(id,ct);var message=conversation.Send(Guid.NewGuid(),current.UserId,request.Body,clock.UtcNow);await communications.Save(ct);var result=Map(message);await realtime.NotifyConversation(id,"message",result,ct);var recipient=current.UserId==conversation.CustomerId?conversation.RunnerId:conversation.CustomerId;await notifications.Publish(recipient,NotificationType.Message,"New message",message.Body.Length>120?message.Body[..120]:message.Body,conversation.ErrandId,ct);return result;}
    public async Task MarkRead(Guid id,Guid messageId,CancellationToken ct)
    {var conversation=await Owned(id,ct);var message=conversation.Messages.SingleOrDefault(x=>x.Id==messageId)??throw new KeyNotFoundException("Message not found.");message.MarkRead(current.UserId,clock.UtcNow);await communications.Save(ct);await realtime.NotifyConversation(id,"messageRead",new{conversationId=id,messageId,readAt=message.ReadAt},ct);}
    public async Task EnsureParticipant(Guid id,CancellationToken ct)=>_ = await Owned(id,ct);
    public async Task<Conversation> Owned(Guid id,CancellationToken ct){var value=await communications.FindConversation(id,ct)??throw new KeyNotFoundException("Conversation not found.");if(!value.Includes(current.UserId))throw new UnauthorizedAccessException();return value;}
    private static MessageDetails Map(ChatMessage value)=>new(value.Id,value.SenderId,value.Body,value.SentAt,value.ReadAt);
    private static ConversationDetails Map(Conversation value)=>new(value.Id,value.ErrandId,value.CustomerId,value.RunnerId,value.Messages.Select(Map).ToList());
    private static ConversationSummary Summary(Conversation value)=>new(value.Id,value.ErrandId,value.CustomerId,value.RunnerId,value.Messages.LastOrDefault() is{} last?Map(last):null);
}

public sealed class VoiceCallService(ICommunicationRepository communications,MessagingService messaging,IRealtimeCommunications realtime,INotificationPublisher notifications,ICurrentUser current,IClock clock)
{
    public async Task<VoiceCallDetails> Start(StartCall request,CancellationToken ct){var conversation=await messaging.Owned(request.ConversationId,ct);var callee=current.UserId==conversation.CustomerId?conversation.RunnerId:conversation.CustomerId;var call=new VoiceCallSession(Guid.NewGuid(),conversation.Id,current.UserId,callee,clock.UtcNow);await communications.AddCall(call,ct);await communications.Save(ct);var result=Map(call);await realtime.NotifyUser(callee,"incomingCall",result,ct);await notifications.Publish(callee,NotificationType.Call,"Incoming voice call","An errand participant is calling you.",conversation.ErrandId,ct);return result;}
    public async Task<VoiceCallDetails> Answer(Guid id,CancellationToken ct){var call=await Owned(id,ct);call.Answer(current.UserId,clock.UtcNow);await SaveAndSignal(call,"callAnswered",ct);return Map(call);}
    public async Task<VoiceCallDetails> Decline(Guid id,CancellationToken ct){var call=await Owned(id,ct);call.Decline(current.UserId,clock.UtcNow);await SaveAndSignal(call,"callDeclined",ct);return Map(call);}
    public async Task<VoiceCallDetails> End(Guid id,EndCall request,CancellationToken ct){var call=await Owned(id,ct);call.End(current.UserId,request.Reason,clock.UtcNow);await SaveAndSignal(call,"callEnded",ct);return Map(call);}
    private async Task<VoiceCallSession> Owned(Guid id,CancellationToken ct){var call=await communications.FindCall(id,ct)??throw new KeyNotFoundException("Call not found.");if(!call.Includes(current.UserId))throw new UnauthorizedAccessException();return call;}
    private async Task SaveAndSignal(VoiceCallSession call,string name,CancellationToken ct){await communications.Save(ct);await realtime.NotifyConversation(call.ConversationId,name,Map(call),ct);}
    private static VoiceCallDetails Map(VoiceCallSession call)=>new(call.Id,call.ConversationId,call.CallerId,call.CalleeId,call.Status,call.CreatedAt,call.AnsweredAt,call.EndedAt,string.IsNullOrEmpty(call.EndReason)?null:call.EndReason);
}
