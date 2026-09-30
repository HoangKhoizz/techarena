using System.Security.Claims;
using ban_link_kien_PC.Domain.Customers;
using ban_link_kien_PC.Infrastructure.Persistence;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Auth;

public sealed record RegisterRequest(string Username, string Email, string Password, string? FullName);
public sealed record LoginRequest(string UsernameOrEmail, string Password);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed class AuthResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public CustomerEntity? Customer { get; init; }

    public static AuthResult Ok(CustomerEntity c) => new() { Succeeded = true, Customer = c };
    public static AuthResult Fail(string error) => new() { Succeeded = false, Error = error };
}

// Facade pattern: gói toàn bộ logic đăng ký/đăng nhập + cookie auth.
public sealed class AuthService
{
    private readonly PcStoreDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IHttpContextAccessor _http;

    public AuthService(PcStoreDbContext db, IPasswordHasher hasher, IHttpContextAccessor http)
    {
        _db = db;
        _hasher = hasher;
        _http = http;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest req, CancellationToken ct = default)
    {
        var username = req.Username.Trim();
        var email = req.Email.Trim().ToLowerInvariant();
        if (await _db.Customers.AnyAsync(c => c.Username == username, ct))
            return AuthResult.Fail("Username đã tồn tại.");
        if (await _db.Customers.AnyAsync(c => c.Email == email, ct))
            return AuthResult.Fail("Email đã tồn tại.");

        var (hash, salt) = _hasher.Hash(req.Password);

        var entity = new CustomerEntity
        {
            Username = username,
            Email = email,
            PasswordHash = hash,
            PasswordSalt = salt,
            FullName = string.IsNullOrWhiteSpace(req.FullName) ? null : req.FullName.Trim(),
            AvatarUrl = null,
            LoyaltyPoints = 0,
            MembershipTier = MembershipTierCatalog.None,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Customers.Add(entity);
        await _db.SaveChangesAsync(ct);

        await SignInAsync(entity, ct);
        return AuthResult.Ok(entity);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest req, CancellationToken ct = default)
    {
        var key = req.UsernameOrEmail.Trim();
        var user = await _db.Customers
            .SingleOrDefaultAsync(c =>
                c.Username == key ||
                c.Email == key.ToLower(), ct);

        if (user is null || !_hasher.Verify(req.Password, user.PasswordHash, user.PasswordSalt))
            return AuthResult.Fail("Tài khoản hoặc mật khẩu không đúng.");
        if (!user.IsActive)
            return AuthResult.Fail("Tài khoản đã bị khóa.");

        await SignInAsync(user, ct);
        return AuthResult.Ok(user);
    }

    public async Task LogoutAsync()
    {
        if (_http.HttpContext is null) return;
        await _http.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public async Task<AuthResult> StartResetPasswordAsync(ForgotPasswordRequest req, CancellationToken ct = default)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Customers.SingleOrDefaultAsync(c => c.Email == email, ct);
        if (user is null)
            return AuthResult.Fail("Email không tồn tại.");

        user.PasswordResetToken = Guid.NewGuid().ToString("N");
        user.PasswordResetExpiresAtUtc = DateTime.UtcNow.AddHours(1);
        await _db.SaveChangesAsync(ct);

        // Demo: token sẽ được hiển thị trên màn hình thay vì gửi email.
        return AuthResult.Ok(user);
    }

    public async Task<AuthResult> ResetPasswordAsync(ResetPasswordRequest req, CancellationToken ct = default)
    {
        var token = req.Token.Trim();
        var user = await _db.Customers.SingleOrDefaultAsync(
            c => c.PasswordResetToken == token, ct);

        if (user is null || user.PasswordResetExpiresAtUtc is null || user.PasswordResetExpiresAtUtc < DateTime.UtcNow)
            return AuthResult.Fail("Mã khôi phục không hợp lệ hoặc đã hết hạn.");

        var (hash, salt) = _hasher.Hash(req.NewPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.PasswordResetToken = null;
        user.PasswordResetExpiresAtUtc = null;
        await _db.SaveChangesAsync(ct);

        return AuthResult.Ok(user);
    }

    public async Task<AuthResult> ChangePasswordAsync(int customerId, ChangePasswordRequest req, CancellationToken ct = default)
    {
        var user = await _db.Customers.SingleOrDefaultAsync(c => c.CustomerId == customerId, ct);
        if (user is null)
            return AuthResult.Fail("Không tìm thấy tài khoản.");
        if (!_hasher.Verify(req.CurrentPassword, user.PasswordHash, user.PasswordSalt))
            return AuthResult.Fail("Mật khẩu hiện tại không đúng.");
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 6)
            return AuthResult.Fail("Mật khẩu mới phải có ít nhất 6 ký tự.");

        var (hash, salt) = _hasher.Hash(req.NewPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.PasswordResetToken = null;
        user.PasswordResetExpiresAtUtc = null;
        await _db.SaveChangesAsync(ct);
        return AuthResult.Ok(user);
    }

    /// <summary>
    /// Đăng nhập / tự đăng ký Customer từ Google (hoặc Facebook sau này) qua email.
    /// </summary>
    public async Task<AuthResult> LoginOrRegisterExternalAsync(
        string provider,
        string email,
        string? fullName,
        string? avatarUrl,
        CancellationToken ct = default)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return AuthResult.Fail($"{provider} không trả về email hợp lệ. Hãy cho phép chia sẻ email.");

        var user = await _db.Customers.SingleOrDefaultAsync(c => c.Email == email, ct);
        if (user is null)
        {
            var username = await AllocateUsernameAsync(email, ct);
            var (hash, salt) = _hasher.Hash(Guid.NewGuid().ToString("N") + Guid.NewGuid());

            user = new CustomerEntity
            {
                Username = username,
                Email = email,
                PasswordHash = hash,
                PasswordSalt = salt,
                FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim(),
                AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl.Trim(),
                LoyaltyPoints = 0,
                MembershipTier = MembershipTierCatalog.None,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.Customers.Add(user);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            if (!user.IsActive)
                return AuthResult.Fail("Tài khoản đã bị khóa.");

            var changed = false;
            if (string.IsNullOrWhiteSpace(user.FullName) && !string.IsNullOrWhiteSpace(fullName))
            {
                user.FullName = fullName.Trim();
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(avatarUrl) &&
                !string.Equals(user.AvatarUrl, avatarUrl, StringComparison.Ordinal))
            {
                user.AvatarUrl = avatarUrl.Trim();
                changed = true;
            }
            if (changed)
                await _db.SaveChangesAsync(ct);
        }

        await SignInAsync(user, ct);
        return AuthResult.Ok(user);
    }

    private async Task<string> AllocateUsernameAsync(string email, CancellationToken ct)
    {
        var local = email.Split('@')[0];
        var cleaned = new string(local.Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '.' or '-').ToArray());
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "user";
        if (cleaned.Length > 40)
            cleaned = cleaned[..40];

        var candidate = cleaned;
        var i = 0;
        while (await _db.Customers.AnyAsync(c => c.Username == candidate, ct))
        {
            i++;
            candidate = $"{cleaned}{i}";
            if (i > 500)
            {
                candidate = $"g_{Guid.NewGuid():N}"[..16];
                break;
            }
        }

        return candidate;
    }

    private async Task SignInAsync(CustomerEntity user, CancellationToken ct)
    {
        if (_http.HttpContext is null) return;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.CustomerId.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new("auth_kind", "customer")
        };

        // Bridge: customer username "admin" vẫn vào portal như SuperAdmin (tương thích cũ)
        if (string.Equals(user.Username, "admin", StringComparison.OrdinalIgnoreCase))
            claims.Add(new Claim(ClaimTypes.Role, StaffRoles.SuperAdmin));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await _http.HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            });
    }
}

