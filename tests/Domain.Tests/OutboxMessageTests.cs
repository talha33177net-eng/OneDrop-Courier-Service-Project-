using Domain.Notifications;

namespace Domain.Tests;

public class OutboxMessageTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_new_message_is_pending_and_due_at_once()
    {
        var message = OutboxMessage.Create("RecipientTextMessage", """{"ParcelId":1}""");

        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Null(message.NextAttemptOn);
        Assert.Equal(0, message.Attempts);
    }

    [Fact]
    public void A_failed_message_sent_again_is_due_at_once_with_fresh_attempts()
    {
        var message = OutboxMessage.Create("RecipientTextMessage", """{"ParcelId":1}""");
        for (var attempt = 0; attempt < OutboxMessage.MaxAttempts; attempt++)
        {
            message.MarkFailed("Gateway down", Now);
        }

        Assert.True(message.SendAgain().IsSuccess);

        Assert.Equal((OutboxStatus.Pending, 0, null), (message.Status, message.Attempts, message.NextAttemptOn));
        Assert.Equal("Gateway down", message.LastError);
        message.MarkFailed("Still down", Now);
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(Now.AddMinutes(1), message.NextAttemptOn);
    }

    [Fact]
    public void Only_a_failed_message_is_sent_again()
    {
        var pending = OutboxMessage.Create("RecipientTextMessage", """{"ParcelId":1}""");
        var sent = OutboxMessage.Create("RecipientTextMessage", """{"ParcelId":1}""");
        sent.MarkSent(Now);
        var skipped = OutboxMessage.Create("ParcelStatusChangedMessage", """{"ParcelId":1}""");
        skipped.MarkSkipped();

        Assert.All(
            new[] { pending, sent, skipped },
            message => Assert.Equal("outbox.notFailed", message.SendAgain().Error!.Code));
        Assert.Equal(OutboxStatus.Sent, sent.Status);
    }

    [Fact]
    public void A_sent_message_records_when()
    {
        var message = OutboxMessage.Create("RecipientTextMessage", """{"ParcelId":1}""");

        message.MarkSent(Now);

        Assert.Equal(OutboxStatus.Sent, message.Status);
        Assert.Equal(Now, message.SentOn);
        Assert.Equal(1, message.Attempts);
    }

    [Fact]
    public void Failures_wait_1_2_4_and_8_minutes_then_the_message_is_given_up()
    {
        var message = OutboxMessage.Create("RecipientTextMessage", """{"ParcelId":1}""");
        var waits = new List<double>();

        for (var attempt = 1; attempt < OutboxMessage.MaxAttempts; attempt++)
        {
            message.MarkFailed("Gateway down", Now);
            waits.Add((message.NextAttemptOn!.Value - Now).TotalMinutes);
            Assert.Equal(OutboxStatus.Pending, message.Status);
        }

        message.MarkFailed("Gateway down", Now);

        Assert.Equal([1, 2, 4, 8], waits);
        Assert.Equal(OutboxStatus.Failed, message.Status);
        Assert.Null(message.NextAttemptOn);
        Assert.Equal(OutboxMessage.MaxAttempts, message.Attempts);
        Assert.Equal("Gateway down", message.LastError);
    }

    [Fact]
    public void A_long_error_is_cut_to_fit_its_column()
    {
        var message = OutboxMessage.Create("RecipientTextMessage", """{"ParcelId":1}""");

        message.MarkFailed(new string('x', 5000), Now);

        Assert.Equal(OutboxMessage.MaxErrorLength, message.LastError!.Length);
    }

    [Theory]
    [InlineData("", "{}")]
    [InlineData("RecipientTextMessage", " ")]
    public void A_message_needs_a_type_and_a_payload(string type, string payload)
    {
        Assert.ThrowsAny<ArgumentException>(() => OutboxMessage.Create(type, payload));
    }
}
