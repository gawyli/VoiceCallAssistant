using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoiceCallAssistant.Models;

namespace VoiceCallAssistant.Repository.Configurations;

public abstract class BaseItemConfiguration<T> : IEntityTypeConfiguration<T>
        where T : BaseEntity
{
    public virtual void Configure(EntityTypeBuilder<T> builder)
    {
    }
}