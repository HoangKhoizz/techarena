using ban_link_kien_PC.Infrastructure.Persistence;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Auth;

public sealed record StaffLoginRequest(string UsernameOrEmail, string Password);

public sealed record CreateStaffRequest(
    string Username,
    string Email,
    string Password,
    string FullName,
    string RoleName,
    string? Phone);

public sealed class StaffLoginResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public string? Token { get; init; }
    public int StaffId { get; init; }
    public string? Username { get; init; }
    public string? FullName { get; init; }
    public string? Role { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }

    public static StaffLoginResult Ok(string token, StaffEntity staff, string role, DateTime expiresAtUtc) => new()
    {
        Succeeded = true,
        Token = token,
        StaffId = staff.StaffId,
        Username = staff.Username,
        FullName = staff.FullName,
        Role = role,
        ExpiresAtUtc = expiresAtUtc
    };

    public static StaffLoginResult Fail(string error) => new() { Succeeded = false, Error = error };
}

public sealed class StaffAuthService
{
    private readonly PcStoreDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly JwtOptions _jwtOptions;

    public StaffAuthService(
        PcStoreDbContext db,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        Microsoft.Extensions.Options.IOptions<JwtOptions> jwtOptions)
    {
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
        _jwtOptions = jwtOptions.Value;
    }

    public async Task<StaffLoginResult> LoginAsync(StaffLoginRequest req, CancellationToken ct = default)
    {
        var key = req.UsernameOrEmail.Trim();
        var staff = await _db.Staffs
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x =>
                x.Username == key || x.Email == key.ToLowerInvariant(), ct);

        if (staff is null || !_hasher.Verify(req.Password, staff.PasswordHash, staff.PasswordSalt))
            return StaffLoginResult.Fail("Tài khoản hoặc mật khẩu không đúng.");
        if (!staff.IsActive)
            return StaffLoginResult.Fail("Tài khoản đã bị khóa.");
        if (staff.Role is null)
            return StaffLoginResult.Fail("Nhân viên chưa được gán Role.");

        var token = _jwt.CreateToken(staff, staff.Role.RoleName);
        var expires = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpireMinutes);
        return StaffLoginResult.Ok(token, staff, staff.Role.RoleName, expires);
    }

    public async Task<(bool ok, string? error, StaffEntity? staff)> CreateStaffAsync(
        CreateStaffRequest req,
        CancellationToken ct = default)
    {
        var username = req.Username.Trim();
        var email = req.Email.Trim().ToLowerInvariant();
        var roleName = req.RoleName.Trim();

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(req.Password) ||
            string.IsNullOrWhiteSpace(req.FullName))
            return (false, "Vui lòng nhập đủ Username, Email, Password, FullName.", null);

        var role = await _db.Roles.SingleOrDefaultAsync(x => x.RoleName == roleName, ct);
        if (role is null)
            return (false, $"Role '{roleName}' không tồn tại.", null);

        if (await _db.Staffs.AnyAsync(x => x.Username == username, ct))
            return (false, "Username đã tồn tại.", null);
        if (await _db.Staffs.AnyAsync(x => x.Email == email, ct))
            return (false, "Email đã tồn tại.", null);

        var (hash, salt) = _hasher.Hash(req.Password);
        var entity = new StaffEntity
        {
            RoleId = role.RoleId,
            Username = username,
            Email = email,
            PasswordHash = hash,
            PasswordSalt = salt,
            FullName = req.FullName.Trim(),
            Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Staffs.Add(entity);
        await _db.SaveChangesAsync(ct);
        entity.Role = role;
        return (true, null, entity);
    }
}
