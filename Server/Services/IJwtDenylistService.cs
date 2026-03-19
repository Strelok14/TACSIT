namespace StrikeballServer.Services;

/// <summary>
/// Сервис denylist для отозванных JWT access-токенов.
/// При logout или принудительном завершении сессии JTI помещается в denylist
/// до истечения исходного срока действия токена.
/// </summary>
public interface IJwtDenylistService
{
    /// <summary>Добавить JTI в denylist с заданным TTL.</summary>
    Task AddAsync(string jti, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>Проверить, находится ли данный JTI в denylist (токен отозван).</summary>
    Task<bool> IsDeniedAsync(string jti, CancellationToken cancellationToken = default);
}
