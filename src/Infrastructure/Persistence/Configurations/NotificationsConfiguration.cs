using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Notifications;

namespace Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.MapTenantOwned(Schemas.Notifications);
        builder.Property(m => m.Type).HasMaxLength(100);
        builder.Property(m => m.Payload).HasMaxLength(1000);
        builder.Property(m => m.LastError).HasMaxLength(OutboxMessage.MaxErrorLength);
        builder.HasIndex(m => new { m.TenantId, m.Status, m.NextAttemptOn })
            .HasDatabaseName("IX_OutboxMessage_Tenant_Status_NextAttemptOn");
    }
}
