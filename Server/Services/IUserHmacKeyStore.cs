namespace StrikeballServer.Services;

public interface IUserHmacKeyStore
{
    Task<string> EnsureKeyBase64Async(int userId, CancellationToken cancellationToken = default);

    Task<byte[]?> GetKeyBytesAsync(int userId, CancellationToken cancellationToken = default);

    Task<string> RotateKeyBase64Async(int userId, CancellationToken cancellationToken = default);
}