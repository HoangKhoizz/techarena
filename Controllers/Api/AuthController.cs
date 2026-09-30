using ban_link_kien_PC.Domain.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ban_link_kien_PC.Controllers.Api;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly StaffAuthService _staffAuth;

    public AuthController(StaffAuthService staffAuth) => _staffAuth = staffAuth;

    /// <summary>Đăng nhập nhân viên — trả JWT chứa Role.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] StaffLoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Vui lòng nhập tài khoản và mật khẩu." });

        var result = await _staffAuth.LoginAsync(request, ct);
        if (!result.Succeeded)
            return Unauthorized(new { error = result.Error });

        return Ok(new
        {
            token = result.Token,
            tokenType = "Bearer",
            expiresAtUtc = result.ExpiresAtUtc,
            staffId = result.StaffId,
            username = result.Username,
            fullName = result.FullName,
            role = result.Role
        });
    }
}
