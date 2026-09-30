using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ban_link_kien_PC.Domain.Auth;

public interface IJwtTokenService
{
    string CreateToken(StaffEntity staff, string roleName);
}

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public string CreateToken(StaffEntity staff, string roleName)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, staff.StaffId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, staff.Username),
            new(ClaimTypes.NameIdentifier, staff.StaffId.ToString()),
            new(ClaimTypes.Name, staff.Username),
            new(ClaimTypes.Email, staff.Email),
            new(ClaimTypes.Role, roleName),
            new("staff_id", staff.StaffId.ToString()),
            new("full_name", staff.FullName)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_options.ExpireMinutes);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
