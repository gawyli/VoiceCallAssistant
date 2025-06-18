using Microsoft.EntityFrameworkCore;
using System.Reflection;
using VoiceCallAssistant.Repository.Configurations;
using VoiceCallAssistant.Utilities;

namespace VoiceCallAssistant.Repository;

public class  CosmosDbConfig
{
    public const string SectionName = "Database";
    public string ConnectionString { get; set; } = null!;
    public string Name { get; set; } = null!;
}

public class CosmosDbContext : DbContext
{
    public CosmosDbContext(DbContextOptions options) : base(options)
    {  
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // All Cosmos items have EF configs that inherit from BaseItemConfiguration<>.
        modelBuilder.ApplyConfigurationsFromAssembly(
            Assembly.GetExecutingAssembly(),
            t => t.InheritsFromGenericParent(typeof(BaseItemConfiguration<>)));
    }

}
