using ban_link_kien_PC.Domain.Auth;
using ban_link_kien_PC.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ban_link_kien_PC.Controllers.Api;

[ApiController]
[Route("api/users")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class UsersController : ControllerBase
{
    private readonly StaffAuthService _staffAuth;

    public UsersController(StaffAuthService staffAuth) => _staffAuth = staffAuth;

    /// <summary>Tạo tài khoản nhân viên — chỉ Admin.</summary>
    [HttpPost("create-staff")]
    [RequireRoles("Admin")]
    public async Task<IActionResult> CreateStaff([FromBody] CreateStaffRequest request, CancellationToken ct)
    {
        var (ok, error, staff) = await _staffAuth.CreateStaffAsync(request, ct);
        if (!ok || staff is null)
            return BadRequest(new { error });

        return Ok(new
        {
            staffId = staff.StaffId,
            username = staff.Username,
            email = staff.Email,
            fullName = staff.FullName,
            role = staff.Role?.RoleName,
            phone = staff.Phone,
            isActive = staff.IsActive
        });
    }
}
