using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoiceCallAssistant.Models;

namespace VoiceCallAssistant.Repository.Configurations;

public class RoutineConfiguration : BaseItemConfiguration<Routine>
{
    public override void Configure(EntityTypeBuilder<Routine> builder)
    {
        base.Configure(builder);

        builder.ToContainer("Routines");
        builder.HasPartitionKey(r => r.Username); //set to id when deployed
        builder.Property(r => r.UserProfileId);
        builder.Property(r => r.Username).HasMaxLength(100);
        builder.Property(r => r.ScheduledTime);
        builder.Property(r => r.PhoneNumber);
        builder.Property(r => r.Name);
        builder.Property(r => r.IsMonFri);
        builder.OwnsOne(r => r.Preferences);

    }
}