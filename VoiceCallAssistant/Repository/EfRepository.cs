using Microsoft.EntityFrameworkCore;
using VoiceCallAssistant.Interfaces;
using VoiceCallAssistant.Models;

namespace VoiceCallAssistant.Repository;

public class EfRepository : IRepository
{
    protected readonly AppDbContext _appDbContext;

    public EfRepository(AppDbContext appDbContext)
    {
        _appDbContext = appDbContext;
    }

    public async Task<T?> GetByIdAsync<T>(string id, CancellationToken cancellationToken) where T : BaseEntity
    {
        return await _appDbContext.Set<T>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<List<T>> ListAsync<T>(CancellationToken cancellationToken) where T : BaseEntity
    {
        return await _appDbContext.Set<T>().ToListAsync(cancellationToken);
    }

    public async Task<T> AddAsync<T>(T entity, CancellationToken cancellationToken) where T : BaseEntity
    {
        if (string.IsNullOrEmpty(entity.Id))
        {
            entity.Id = Guid.NewGuid().ToString();
        }

        await _appDbContext.Set<T>().AddAsync(entity, cancellationToken);
        await _appDbContext.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task UpdateAsync<T>(T entity, CancellationToken cancellationToken) where T : BaseEntity
    {
        _appDbContext.Entry(entity).State = EntityState.Modified;
        await _appDbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync<T>(T entity, CancellationToken cancellationToken) where T : BaseEntity
    {
        _appDbContext.Set<T>().Remove(entity);
        await _appDbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountAsync<T>(CancellationToken cancellationToken) where T : BaseEntity
    {
        return _appDbContext.Set<T>().CountAsync(cancellationToken);
    }

    public void Detach<T>(T entity) where T : BaseEntity
    {
        _appDbContext.Entry(entity).State = EntityState.Detached;
    }
}
