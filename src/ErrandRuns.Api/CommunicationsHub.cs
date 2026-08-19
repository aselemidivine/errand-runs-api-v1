using System.Security.Claims;
using System.Text.Json;
using ErrandRuns.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ErrandRuns.Api;

[Authorize]
public sealed class CommunicationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var id = Context.User?.FindFirstValue("sub")
            ?? throw new HubException("Authenticated user ID is missing.");
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(Guid.Parse(id)));
        await base.OnConnectedAsync();
    }

    public async Task JoinConversation(Guid conversationId, MessagingService messaging)
    {
        await messaging.EnsureParticipant(conversationId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));
    }

    public async Task RelayCallSignal(Guid conversationId, string signalType, JsonElement payload,
        MessagingService messaging)
    {
        if (signalType is not ("offer" or "answer" or "iceCandidate"))
            throw new HubException("Unsupported call signal type.");
        await messaging.EnsureParticipant(conversationId, Context.ConnectionAborted);
        await Clients.OthersInGroup(ConversationGroup(conversationId))
            .SendAsync("callSignal", new { conversationId, signalType, payload }, Context.ConnectionAborted);
    }

    internal static string UserGroup(Guid id) => "user:" + id.ToString("N");
    internal static string ConversationGroup(Guid id) => "conversation:" + id.ToString("N");
}

public sealed class SignalRCommunications(IHubContext<CommunicationsHub> hub) : IRealtimeCommunications
{
    public Task NotifyUser(Guid userId, string eventName, object payload, CancellationToken ct) =>
        hub.Clients.Group(CommunicationsHub.UserGroup(userId)).SendAsync(eventName, payload, ct);

    public Task NotifyConversation(Guid conversationId, string eventName, object payload, CancellationToken ct) =>
        hub.Clients.Group(CommunicationsHub.ConversationGroup(conversationId)).SendAsync(eventName, payload, ct);
}
