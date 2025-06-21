using Microsoft.EntityFrameworkCore;
using VoiceCallAssistant.Interfaces;
using VoiceCallAssistant.Models;

namespace VoiceCallAssistant.Repository;

public class EfRepository : IRepository
{
    //protected readonly IClock? _clock;
    protected readonly CosmosDbContext? _cosmosDbContext;

    public EfRepository(CosmosDbContext? cosmosDbContext = null)
    {
        //_clock = clock;
        _cosmosDbContext = cosmosDbContext;
    }

    public async Task<T?> GetByIdAsync<T>(string id, CancellationToken cancellationToken) where T : BaseEntity
    {
        return await GetDbContext<T>().Set<T>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<List<T>> ListAsync<T>(CancellationToken cancellationToken) where T : BaseEntity
    {
        return await GetDbContext<T>().Set<T>().ToListAsync(cancellationToken);
    }

    public async Task<T> AddAsync<T>(T entity, CancellationToken cancellationToken) where T : BaseEntity
    {
        if (string.IsNullOrEmpty(entity.Id))
        {
            entity.Id = Guid.NewGuid().ToString();
        }

        await GetDbContext<T>().Set<T>().AddAsync(entity, cancellationToken);
        await GetDbContext<T>().SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task UpdateAsync<T>(T entity, CancellationToken cancellationToken) where T : BaseEntity//, IAggregateRoot
    {
        //entity.OnUpdated(_clock?.CurrentDateTime);
        GetDbContext<T>().Entry(entity).State = EntityState.Modified;
        await GetDbContext<T>().SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync<T>(T entity, CancellationToken cancellationToken) where T : BaseEntity//, IAggregateRoot
    {
        GetDbContext<T>().Set<T>().Remove(entity);
        await GetDbContext<T>().SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountAsync<T>(CancellationToken cancellationToken) where T : BaseEntity
    {
        return GetDbContext<T>().Set<T>().CountAsync(cancellationToken);
    }

    public void Detach<T>(T entity) where T : BaseEntity//, IAggregateRoot
    {
        GetDbContext<T>().Entry(entity).State = EntityState.Detached;
    }

    private DbContext GetDbContext<T>()
    {
        if ((_cosmosDbContext != null) && (_cosmosDbContext.Model.FindEntityType(typeof(T)) != null))
        {
            return _cosmosDbContext;
        }
        else
        {
            throw new ArgumentNullException($"No DbContext configured for type {typeof(T).Name}");
        }
    }
}
