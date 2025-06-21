using VoiceCallAssistant.Models;

namespace VoiceCallAssistant.Interfaces;

public interface IRepository
{
    Task<T> AddAsync<T>(T entity, CancellationToken cancellationToken) where T : BaseEntity;
    Task UpdateAsync<T>(T entity, CancellationToken cancellationToken) where T : BaseEntity;
    Task DeleteAsync<T>(T entity, CancellationToken cancellationToken) where T : BaseEntity;

    // Read
    Task<T?> GetByIdAsync<T>(string id, CancellationToken cancellationToken) where T : BaseEntity;
    Task<List<T>> ListAsync<T>(CancellationToken cancellationToken) where T : BaseEntity;
    Task<int> CountAsync<T>(CancellationToken cancellationToken) where T : BaseEntity;
}