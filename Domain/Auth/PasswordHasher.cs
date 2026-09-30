using System.Security.Cryptography;
using System.Text;

namespace ban_link_kien_PC.Domain.Auth;

// Strategy pattern: cho phép thay đổi thuật toán hash mật khẩu dễ dàng.
public interface IPasswordHasher
{
    (byte[] hash, byte[] salt) Hash(string password);
    bool Verify(string password, byte[] hash, byte[] salt);
}

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 100_000;
    private const int HashSize = 32;
    private const int SaltSize = 16;

    public (byte[] hash, byte[] salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(HashSize);
        return (hash, salt);
    }

    public bool Verify(string password, byte[] hash, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
        var computed = pbkdf2.GetBytes(HashSize);
        return CryptographicOperations.FixedTimeEquals(computed, hash);
    }
}

