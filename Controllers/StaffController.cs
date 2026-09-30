using ban_link_kien_PC.Domain.Auth;
using ban_link_kien_PC.Infrastructure.Persistence;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Controllers;

[Authorize]
public sealed class StaffController : Controller
{
    private readonly PcStoreDbContext _db;
    private readonly IPasswordHasher _hasher;

    public StaffController(PcStoreDbContext db, IPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? keyword, int? roleId, string? status, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        ViewData["Title"] = "Quản lý nhân viên";

        var query = _db.Staffs.AsNoTracking().Include(x => x.Role).AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(x =>
                x.FullName.Contains(key) ||
                x.Username.Contains(key) ||
                x.Email.Contains(key) ||
                (x.Phone != null && x.Phone.Contains(key)));
        }

        if (roleId is > 0)
            query = query.Where(x => x.RoleId == roleId);

        if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.IsActive);
        else if (string.Equals(status, "inactive", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => !x.IsActive);

        var rows = await query
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.FullName)
            .Select(x => new StaffListItemViewModel
            {
                StaffId = x.StaffId,
                FullName = x.FullName,
                Username = x.Username,
                Email = x.Email,
                Phone = x.Phone,
                RoleId = x.RoleId,
                RoleName = x.Role != null ? x.Role.RoleName : "",
                IsActive = x.IsActive,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(ct);

        var roles = await LoadRoleOptionsAsync(ct);
        var vm = new StaffIndexViewModel
        {
            Keyword = keyword?.Trim(),
            RoleId = roleId,
            Status = status,
            Rows = rows,
            RoleOptions = roles,
            CreateForm = new StaffCreateViewModel { RoleOptions = roles },
            EditForm = new StaffEditViewModel { RoleOptions = roles }
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StaffCreateViewModel model, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        model.FullName = model.FullName?.Trim() ?? "";
        model.Username = model.Username?.Trim() ?? "";
        model.Email = model.Email?.Trim().ToLowerInvariant() ?? "";
        model.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();

        if (string.IsNullOrWhiteSpace(model.FullName) ||
            string.IsNullOrWhiteSpace(model.Username) ||
            string.IsNullOrWhiteSpace(model.Email) ||
            string.IsNullOrWhiteSpace(model.Password))
        {
            TempData["AdminError"] = "Vui lòng nhập đầy đủ Họ tên, Username, Email và Mật khẩu.";
            return RedirectToAction(nameof(Index));
        }

        if (model.Password.Length < 6)
        {
            TempData["AdminError"] = "Mật khẩu phải có ít nhất 6 ký tự.";
            return RedirectToAction(nameof(Index));
        }

        if (model.RoleId <= 0)
        {
            model.RoleId = await _db.Roles
                .Where(x => x.RoleName == StaffRoles.DefaultNewStaffRole)
                .Select(x => x.RoleId)
                .FirstOrDefaultAsync(ct);
        }

        if (model.RoleId <= 0 || !await _db.Roles.AnyAsync(x => x.RoleId == model.RoleId, ct))
        {
            TempData["AdminError"] = "Vai trò không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }

        if (await _db.Staffs.AnyAsync(x => x.Username == model.Username, ct))
        {
            TempData["AdminError"] = "Username đã tồn tại.";
            return RedirectToAction(nameof(Index));
        }

        if (await _db.Staffs.AnyAsync(x => x.Email == model.Email, ct))
        {
            TempData["AdminError"] = "Email đã tồn tại.";
            return RedirectToAction(nameof(Index));
        }

        var (hash, salt) = _hasher.Hash(model.Password);
        _db.Staffs.Add(new StaffEntity
        {
            FullName = model.FullName,
            Username = model.Username,
            Email = model.Email,
            Phone = model.Phone,
            RoleId = model.RoleId,
            PasswordHash = hash,
            PasswordSalt = salt,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        TempData["AdminInfo"] = $"Đã tạo nhân viên '{model.Username}' thành công.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(StaffEditViewModel model, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var staff = await _db.Staffs.SingleOrDefaultAsync(x => x.StaffId == model.StaffId, ct);
        if (staff is null)
        {
            TempData["AdminError"] = "Không tìm thấy nhân viên.";
            return RedirectToAction(nameof(Index));
        }

        model.FullName = model.FullName?.Trim() ?? "";
        model.Email = model.Email?.Trim().ToLowerInvariant() ?? "";
        model.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();

        if (string.IsNullOrWhiteSpace(model.FullName) ||
            string.IsNullOrWhiteSpace(model.Email) ||
            model.RoleId <= 0)
        {
            TempData["AdminError"] = "Vui lòng nhập đầy đủ Họ tên, Email và Vai trò.";
            return RedirectToAction(nameof(Index));
        }

        if (!await _db.Roles.AnyAsync(x => x.RoleId == model.RoleId, ct))
        {
            TempData["AdminError"] = "Vai trò không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }

        if (await _db.Staffs.AnyAsync(x => x.Email == model.Email && x.StaffId != staff.StaffId, ct))
        {
            TempData["AdminError"] = "Email đã được dùng bởi tài khoản khác.";
            return RedirectToAction(nameof(Index));
        }

        // Không cho hạ cấp / khóa admin cuối cùng
        var wasAdmin = await IsAdminRoleAsync(staff.RoleId, ct);
        var willBeAdmin = await IsAdminRoleAsync(model.RoleId, ct);
        if (wasAdmin && !willBeAdmin && await CountActiveAdminsAsync(ct) <= 1 && staff.IsActive)
        {
            TempData["AdminError"] = "Không thể đổi vai trò Admin cuối cùng đang hoạt động.";
            return RedirectToAction(nameof(Index));
        }

        staff.FullName = model.FullName;
        staff.Email = model.Email;
        staff.Phone = model.Phone;
        staff.RoleId = model.RoleId;

        await _db.SaveChangesAsync(ct);
        TempData["AdminInfo"] = $"Đã cập nhật nhân viên '{staff.Username}'.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int staffId, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var staff = await _db.Staffs.Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.StaffId == staffId, ct);
        if (staff is null)
        {
            TempData["AdminError"] = "Không tìm thấy nhân viên.";
            return RedirectToAction(nameof(Index));
        }

        if (staff.IsActive &&
            string.Equals(staff.Role?.RoleName, "Admin", StringComparison.OrdinalIgnoreCase) &&
            await CountActiveAdminsAsync(ct) <= 1)
        {
            TempData["AdminError"] = "Không thể khóa Admin cuối cùng đang hoạt động.";
            return RedirectToAction(nameof(Index));
        }

        staff.IsActive = !staff.IsActive;
        await _db.SaveChangesAsync(ct);
        TempData["AdminInfo"] = staff.IsActive
            ? $"Đã kích hoạt lại '{staff.Username}'."
            : $"Đã khóa tài khoản '{staff.Username}'.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int staffId, string newPassword, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var staff = await _db.Staffs.SingleOrDefaultAsync(x => x.StaffId == staffId, ct);
        if (staff is null)
        {
            TempData["AdminError"] = "Không tìm thấy nhân viên.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            TempData["AdminError"] = "Mật khẩu mới phải có ít nhất 6 ký tự.";
            return RedirectToAction(nameof(Index));
        }

        var (hash, salt) = _hasher.Hash(newPassword);
        staff.PasswordHash = hash;
        staff.PasswordSalt = salt;
        await _db.SaveChangesAsync(ct);

        TempData["AdminInfo"] = $"Đã đặt lại mật khẩu cho '{staff.Username}'.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<SelectListItem>> LoadRoleOptionsAsync(CancellationToken ct)
    {
        var portal = StaffRoles.PortalRoles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roles = await _db.Roles.AsNoTracking()
            .Where(x => portal.Contains(x.RoleName))
            .OrderBy(x => x.RoleName)
            .Select(x => new { x.RoleId, x.RoleName })
            .ToListAsync(ct);

        var defaultId = roles.FirstOrDefault(x =>
            string.Equals(x.RoleName, StaffRoles.DefaultNewStaffRole, StringComparison.OrdinalIgnoreCase))?.RoleId;

        return roles.Select(x => new SelectListItem
        {
            Value = x.RoleId.ToString(),
            Text = x.RoleName,
            Selected = defaultId.HasValue && x.RoleId == defaultId.Value
        }).ToList();
    }

    private async Task<bool> IsAdminRoleAsync(int roleId, CancellationToken ct)
    {
        return await _db.Roles.AnyAsync(
            x => x.RoleId == roleId &&
                 (x.RoleName == StaffRoles.SuperAdmin || x.RoleName == StaffRoles.LegacyAdmin), ct);
    }

    private async Task<int> CountActiveAdminsAsync(CancellationToken ct)
    {
        return await _db.Staffs.CountAsync(
            x => x.IsActive && x.Role != null &&
                 (x.Role.RoleName == StaffRoles.SuperAdmin || x.Role.RoleName == StaffRoles.LegacyAdmin), ct);
    }

    private IActionResult? EnsureAdmin() =>
        AdminPortalAccess.EnsureCanAccess(this, User, nameof(Index), "Staff");
}

public sealed class StaffIndexViewModel
{
    public string? Keyword { get; set; }
    public int? RoleId { get; set; }
    public string? Status { get; set; }
    public List<StaffListItemViewModel> Rows { get; set; } = [];
    public List<SelectListItem> RoleOptions { get; set; } = [];
    public StaffCreateViewModel CreateForm { get; set; } = new();
    public StaffEditViewModel EditForm { get; set; } = new();
}

public sealed class StaffListItemViewModel
{
    public int StaffId { get; set; }
    public string FullName { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public int RoleId { get; set; }
    public string RoleName { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class StaffCreateViewModel
{
    public string FullName { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Phone { get; set; }
    public int RoleId { get; set; }
    public List<SelectListItem> RoleOptions { get; set; } = [];
}

public sealed class StaffEditViewModel
{
    public int StaffId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public int RoleId { get; set; }
    public List<SelectListItem> RoleOptions { get; set; } = [];
}
