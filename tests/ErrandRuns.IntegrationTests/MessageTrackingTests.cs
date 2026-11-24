using ErrandRuns.Domain.Communications;
using ErrandRuns.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ErrandRuns.IntegrationTests;
public sealed class MessageTrackingTests
{
    [Fact]
    public void New_client_generated_message_is_inserted_not_updated()
    {
        using var db = new ErrandRunsDbContext(new DbContextOptionsBuilder<ErrandRunsDbContext>()
            .UseSqlServer("Server=localhost;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True").Options);
        var customer = Guid.NewGuid();
        var conversation = new Conversation(Guid.NewGuid(), Guid.NewGuid(), customer, Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.Attach(conversation);
        var message = conversation.Send(Guid.NewGuid(), customer, "Arrived", DateTimeOffset.UtcNow);
        new CommunicationRepository(db).TrackNewMessage(message);
        db.ChangeTracker.DetectChanges();
        Assert.Equal(EntityState.Added, db.Entry(message).State);
        Assert.Equal(EntityState.Unchanged, db.Entry(conversation).State);
    }
}
