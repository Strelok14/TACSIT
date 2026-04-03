using Microsoft.EntityFrameworkCore;
using StrikeballServer.Data;
using StrikeballServer.Models;
using System.Security.Cryptography;

namespace StrikeballServer.Services;

public class UserHmacKeyStore : IUserHmacKeyStore
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<UserHmacKeyStore> _logger;
    private readonly byte[] _masterKey;

    public UserHmacKeyStore(ApplicationDbContext context, IConfiguration configuration, ILogger<UserHmacKeyStore> logger)
    {
        _context = context;
        _logger = logger;

        var masterKeyB64 = configuration["Security:MasterKeyB64"]
            ?? Environment.GetEnvironmentVariable("TACID_MASTER_KEY_B64")
            ?? throw new InvalidOperationException("Master key is required");

        _masterKey = Convert.FromBase64String(masterKeyB64);
        if (_masterKey.Length != 32)
        {
            throw new InvalidOperationException("Master key must decode to 32 bytes");
        }
    }

    public async Task<string> EnsureKeyBase64Async(int userId, CancellationToken cancellationToken = default)
    {
        var existing = await GetKeyBytesAsync(userId, cancellationToken);
        if (existing != null)
        {
            return Convert.ToBase64String(existing);
        }

        return await RotateKeyBase64Async(userId, cancellationToken);
    }

    public async Task<byte[]?> GetKeyBytesAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null || user.HmacKeyCiphertext == null || user.HmacKeyNonce == null || user.HmacKeyTag == null)
        {
            return null;
        }

        try
        {
            var plaintext = new byte[user.HmacKeyCiphertext.Length];
            using var aes = new AesGcm(_masterKey, 16);
            aes.Decrypt(user.HmacKeyNonce, user.HmacKeyCiphertext, user.HmacKeyTag, plaintext);
            return plaintext;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt HMAC key for user {userId}", userId);
            return null;
        }
    }

    public async Task<string> RotateKeyBase64Async(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} not found");

        var rawKey = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[rawKey.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(_masterKey, 16))
        {
            aes.Encrypt(nonce, rawKey, ciphertext, tag);
        }

        user.HmacKeyNonce = nonce;
        user.HmacKeyCiphertext = ciphertext;
        user.HmacKeyTag = tag;
        await _context.SaveChangesAsync(cancellationToken);

        return Convert.ToBase64String(rawKey);
    }
}