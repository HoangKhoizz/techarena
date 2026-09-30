using ban_link_kien_PC.Domain.Auth;
using ban_link_kien_PC.Domain.Customers;
using ban_link_kien_PC.Infrastructure.Persistence;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Controllers;

[Authorize]
public sealed class PromoCodesController : Controller
{
    private readonly PcStoreDbContext _db;

    public PromoCodesController(PcStoreDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        ViewData["Title"] = "Mã khuyến mãi";
        var rows = await _db.PromoCodes.AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new PromoCodeListRow
            {
                PromoCodeId = x.PromoCodeId,
                Code = x.Code,
                DiscountType = x.DiscountType,
                DiscountValue = x.DiscountValue,
                MinOrderVnd = x.MinOrderVnd,
                MaxUses = x.MaxUses,
                UsedCount = x.UsedCount,
                StartsAtUtc = x.StartsAtUtc,
                ExpiresAtUtc = x.ExpiresAtUtc,
                ApplicableTier = x.ApplicableTier,
                IsActive = x.IsActive,
                Description = x.Description
            })
            .ToListAsync(ct);

        foreach (var r in rows)
        {
            r.DiscountDisplay = r.DiscountType.Equals("FIXED", StringComparison.OrdinalIgnoreCase)
                ? $"{r.DiscountValue:N0}₫"
                : $"{r.DiscountValue:0.#}%";
            r.TierDisplay = string.IsNullOrWhiteSpace(r.ApplicableTier)
                ? "Tất cả"
                : MembershipTierCatalog.ToDisplayName(r.ApplicableTier);
        }

        return View(rows);
    }

    [HttpGet]
    public IActionResult Create()
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;
        ViewData["Title"] = "Thêm mã KM";
        return View(new PromoCodeEditViewModel
        {
            IsActive = true,
            DiscountType = "PERCENT",
            DiscountValue = 10,
            StartsAtUtc = DateTime.UtcNow.Date
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PromoCodeEditViewModel vm, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        if (!Validate(vm))
            return View(vm);

        var code = vm.Code.Trim().ToUpperInvariant();
        if (await _db.PromoCodes.AnyAsync(x => x.Code == code, ct))
        {
            ModelState.AddModelError(nameof(vm.Code), "Mã đã tồn tại.");
            return View(vm);
        }

        _db.PromoCodes.Add(MapEntity(vm, new PromoCodeEntity { CreatedAtUtc = DateTime.UtcNow, UsedCount = 0 }));
        await _db.SaveChangesAsync(ct);
        TempData["AdminInfo"] = $"Đã tạo mã {code}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var x = await _db.PromoCodes.AsNoTracking().SingleOrDefaultAsync(p => p.PromoCodeId == id, ct);
        if (x is null) return NotFound();

        ViewData["Title"] = $"Sửa mã {x.Code}";
        return View(new PromoCodeEditViewModel
        {
            PromoCodeId = x.PromoCodeId,
            Code = x.Code,
            DiscountType = x.DiscountType,
            DiscountValue = x.DiscountValue,
            MinOrderVnd = x.MinOrderVnd,
            MaxUses = x.MaxUses,
            StartsAtUtc = x.StartsAtUtc,
            ExpiresAtUtc = x.ExpiresAtUtc,
            ApplicableTier = x.ApplicableTier,
            IsActive = x.IsActive,
            Description = x.Description,
            UsedCount = x.UsedCount
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(PromoCodeEditViewModel vm, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        if (!Validate(vm))
            return View(vm);

        var entity = await _db.PromoCodes.SingleOrDefaultAsync(x => x.PromoCodeId == vm.PromoCodeId, ct);
        if (entity is null) return NotFound();

        var code = vm.Code.Trim().ToUpperInvariant();
        if (await _db.PromoCodes.AnyAsync(x => x.PromoCodeId != vm.PromoCodeId && x.Code == code, ct))
        {
            ModelState.AddModelError(nameof(vm.Code), "Mã đã tồn tại.");
            return View(vm);
        }

        MapEntity(vm, entity);
        await _db.SaveChangesAsync(ct);
        TempData["AdminInfo"] = "Đã cập nhật mã khuyến mãi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var row = await _db.PromoCodes.SingleOrDefaultAsync(x => x.PromoCodeId == id, ct);
        if (row is null) return NotFound();
        row.IsActive = !row.IsActive;
        await _db.SaveChangesAsync(ct);
        TempData["AdminInfo"] = row.IsActive ? $"Đã bật mã {row.Code}." : $"Đã tắt mã {row.Code}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var row = await _db.PromoCodes.SingleOrDefaultAsync(x => x.PromoCodeId == id, ct);
        if (row is null) return NotFound();

        var used = await _db.Orders.AnyAsync(o => o.PromoCodeId == id, ct);
        if (used)
        {
            row.IsActive = false;
            await _db.SaveChangesAsync(ct);
            TempData["AdminInfo"] = $"Mã {row.Code} đã dùng trên đơn — chỉ tắt, không xóa cứng.";
        }
        else
        {
            _db.PromoCodes.Remove(row);
            await _db.SaveChangesAsync(ct);
            TempData["AdminInfo"] = $"Đã xóa mã {row.Code}.";
        }

        return RedirectToAction(nameof(Index));
    }

    private bool Validate(PromoCodeEditViewModel vm)
    {
        vm.Code = (vm.Code ?? "").Trim().ToUpperInvariant();
        vm.DiscountType = (vm.DiscountType ?? "PERCENT").Trim().ToUpperInvariant();
        vm.Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim();
        vm.ApplicableTier = string.IsNullOrWhiteSpace(vm.ApplicableTier)
            ? null
            : MembershipTierCatalog.Normalize(vm.ApplicableTier);
        if (vm.ApplicableTier == MembershipTierCatalog.None)
            vm.ApplicableTier = null;

        if (string.IsNullOrWhiteSpace(vm.Code))
            ModelState.AddModelError(nameof(vm.Code), "Nhập mã khuyến mãi.");
        if (vm.DiscountType is not ("PERCENT" or "FIXED"))
            ModelState.AddModelError(nameof(vm.DiscountType), "Loại giảm giá không hợp lệ.");
        if (vm.DiscountValue <= 0)
            ModelState.AddModelError(nameof(vm.DiscountValue), "Giá trị giảm phải > 0.");
        if (vm.DiscountType == "PERCENT" && vm.DiscountValue > 100)
            ModelState.AddModelError(nameof(vm.DiscountValue), "Phần trăm tối đa 100.");
        if (vm.ExpiresAtUtc is DateTime exp && vm.StartsAtUtc is DateTime start && exp < start)
            ModelState.AddModelError(nameof(vm.ExpiresAtUtc), "Ngày hết hạn phải sau ngày bắt đầu.");

        return ModelState.IsValid;
    }

    private static PromoCodeEntity MapEntity(PromoCodeEditViewModel vm, PromoCodeEntity e)
    {
        e.Code = vm.Code.Trim().ToUpperInvariant();
        e.DiscountType = vm.DiscountType.Trim().ToUpperInvariant();
        e.DiscountValue = vm.DiscountValue;
        e.MinOrderVnd = vm.MinOrderVnd;
        e.MaxUses = vm.MaxUses;
        e.StartsAtUtc = vm.StartsAtUtc?.ToUniversalTime();
        e.ExpiresAtUtc = vm.ExpiresAtUtc?.ToUniversalTime();
        e.ApplicableTier = string.IsNullOrWhiteSpace(vm.ApplicableTier) ? null : vm.ApplicableTier;
        e.IsActive = vm.IsActive;
        e.Description = vm.Description;
        return e;
    }

    private IActionResult? EnsureAdmin() =>
        AdminPortalAccess.EnsureCanAccess(this, User, nameof(Index), "PromoCodes");
}

public sealed class PromoCodeListRow
{
    public int PromoCodeId { get; set; }
    public string Code { get; set; } = "";
    public string DiscountType { get; set; } = "";
    public decimal DiscountValue { get; set; }
    public string DiscountDisplay { get; set; } = "";
    public decimal? MinOrderVnd { get; set; }
    public int? MaxUses { get; set; }
    public int UsedCount { get; set; }
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string? ApplicableTier { get; set; }
    public string TierDisplay { get; set; } = "";
    public bool IsActive { get; set; }
    public string? Description { get; set; }
}

public sealed class PromoCodeEditViewModel
{
    public int PromoCodeId { get; set; }
    public string Code { get; set; } = "";
    public string DiscountType { get; set; } = "PERCENT";
    public decimal DiscountValue { get; set; }
    public decimal? MinOrderVnd { get; set; }
    public int? MaxUses { get; set; }
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string? ApplicableTier { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public int UsedCount { get; set; }

    public List<SelectListItem> DiscountTypeOptions { get; } =
    [
        new("Phần trăm (%)", "PERCENT"),
        new("Số tiền cố định (₫)", "FIXED")
    ];

    public List<SelectListItem> TierOptions { get; } =
    [
        new("Tất cả khách", ""),
        new("Thẻ Bạc trở lên", "SILVER"),
        new("Thẻ Vàng trở lên", "GOLD"),
        new("Thẻ Bạch kim", "PLATINUM")
    ];
}
