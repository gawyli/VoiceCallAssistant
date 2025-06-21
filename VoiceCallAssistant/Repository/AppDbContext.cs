using Microsoft.EntityFrameworkCore;
using System.Reflection;
using VoiceCallAssistant.Repository.Configurations;
using VoiceCallAssistant.Utilities;

namespace VoiceCallAssistant.Repository;

public class  DatabaseConfig
{
    public const string SectionName = "Database";
    public string ConnectionString { get; set; } = null!;
    public string Name { get; set; } = null!;
}

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions options) : base(options)
    {  
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            Assembly.GetExecutingAssembly(),
            t => t.InheritsFromGenericParent(typeof(BaseItemConfiguration<>)));
    }

}
