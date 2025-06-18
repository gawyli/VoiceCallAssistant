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

    public async Task<T> AddAsync<T>(T entity, CancellationToken cancellationToken) where T : BaseEntity//, IAggregateRoot
    {
        if (string.IsNullOrEmpty(entity.Id))
        {
            entity.Id = Guid.NewGuid().ToString();
        }
        //entity.OnCreated(_clock?.CurrentDateTime);
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

    //public async Task<T?> GetBySpecAsync<T, TSpec>(TSpec specification, CancellationToken cancellationToken) where TSpec : ISingleResultSpecification, ISpecification<T> where T : BaseEntity
    //{
    //    var specificationResult = ApplySpecification(specification);
    //    return await specificationResult.FirstOrDefaultAsync(cancellationToken);
    //}

    //public async Task<TResult?> GetBySpecAsync<T, TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken) where T : BaseEntity
    //{
    //    return await ApplySpecification(specification).FirstOrDefaultAsync(cancellationToken);
    //}

    //public async Task<List<T>> ListAsync<T>(ISpecification<T> specification, CancellationToken cancellationToken) where T : BaseEntity
    //{
    //    var specificationResult = ApplySpecification(specification);
    //    return await specificationResult.ToListAsync(cancellationToken);
    //}

    /// <inheritdoc/>
    //public Task<int> CountAsync<T>(ISpecification<T> specification, CancellationToken cancellationToken) where T : BaseEntity
    //{
    //    return ApplySpecification(specification, true).CountAsync(cancellationToken);
    //}

    /// <inheritdoc/>

    //private IQueryable<T> ApplySpecification<T>(ISpecification<T> specification, bool evaluateCriteriaOnly = false) where T : BaseEntity
    //{
    //    return SpecificationEvaluator.Default.GetQuery(GetDbContext<T>().Set<T>().AsQueryable(), specification, evaluateCriteriaOnly);
    //}

    //private IQueryable<TResult> ApplySpecification<T, TResult>(ISpecification<T, TResult> specification) where T : BaseEntity
    //{
    //    if (specification is null)
    //    {
    //        throw new ArgumentNullException("Specification is required");
    //    }

    //    if (specification.Selector is null)
    //    {
    //        throw new SelectorNotFoundException();
    //    }

    //    return SpecificationEvaluator.Default.GetQuery(GetDbContext<T>().Set<T>().AsQueryable(), specification);
    //}

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
