using VoiceCallAssistant.Models;

namespace VoiceCallAssistant.Interfaces;

public interface IRepository
{
    Task<T> AddAsync<T>(T entity, CancellationToken cancellationToken) where T : BaseEntity;//, IAggregateRoot;
    Task UpdateAsync<T>(T entity, CancellationToken cancellationToken) where T : BaseEntity;//, IAggregateRoot;
    Task DeleteAsync<T>(T entity, CancellationToken cancellationToken) where T : BaseEntity;//, IAggregateRoot;

    // Read
    Task<T?> GetByIdAsync<T>(string id, CancellationToken cancellationToken) where T : BaseEntity;
    Task<List<T>> ListAsync<T>(CancellationToken cancellationToken) where T : BaseEntity;
    Task<int> CountAsync<T>(CancellationToken cancellationToken) where T : BaseEntity;

    //Task<T?> GetBySpecAsync<T, TSpec>(TSpec specification, CancellationToken cancellationToken) where TSpec : ISingleResultSpecification, ISpecification<T> where T : BaseEntity;
    //Task<TResult?> GetBySpecAsync<T, TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken) where T : BaseEntity;
    //Task<List<T>> ListAsync<T>(ISpecification<T> spec, CancellationToken cancellationToken) where T : BaseEntity;
    //Task<int> CountAsync<T>(ISpecification<T> specification, CancellationToken cancellationToken) where T : BaseEntity;
}