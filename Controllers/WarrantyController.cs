using ban_link_kien_PC.Domain.Cskh;
using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Infrastructure.Persistence;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ban_link_kien_PC.Controllers;

[Authorize]
public sealed class WarrantyController : Controller
{
    private readonly PcStoreDbContext _db;

    public WarrantyController(PcStoreDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!TryGetCustomerId(out var customerId))
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Index)) });

        ViewData["Title"] = "Bảo hành của tôi";
        var rows = await (
            from w in _db.WarrantyClaims.AsNoTracking()
            join c in _db.Components.AsNoTracking() on w.ComponentId equals c.ComponentId
            where w.CustomerId == customerId
            orderby w.CreatedAtUtc descending
            select new CustomerWarrantyRow
            {
                WarrantyClaimId = w.WarrantyClaimId,
                OrderId = w.OrderId,
                ProductName = c.Name,
                Sku = c.Sku,
                SerialNumber = w.SerialNumber,
                IssueDescription = w.IssueDescription,
                Status = w.Status,
                AdminNote = w.AdminNote,
                CreatedAtUtc = w.CreatedAtUtc,
                ResolvedAtUtc = w.ResolvedAtUtc
            }
        ).ToListAsync(ct);

        foreach (var r in rows)
        {
            r.StatusDisplay = WarrantyClaimStatus.ToDisplayName(r.Status);
            r.StatusBadgeClass = WarrantyClaimStatus.ToBadgeClass(r.Status);
        }

        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        if (!TryGetCustomerId(out var customerId))
            return RedirectToAction("Login", "Account");

        ViewData["Title"] = "Yêu cầu bảo hành";
        var vm = new CreateWarrantyViewModel();
        await FillEligibleItemsAsync(vm, customerId, ct);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateWarrantyViewModel vm, CancellationToken ct)
    {
        if (!TryGetCustomerId(out var customerId))
            return RedirectToAction("Login", "Account");

        if (!string.IsNullOrWhiteSpace(vm.SelectedItem))
        {
            var parts = vm.SelectedItem.Split('|');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var oid) &&
                int.TryParse(parts[1], out var cid))
            {
                vm.OrderId = oid;
                vm.ComponentId = cid;
            }
        }

        if (vm.OrderId <= 0 || vm.ComponentId <= 0)
            ModelState.AddModelError("", "Chọn đơn hàng và sản phẩm.");
        if (string.IsNullOrWhiteSpace(vm.IssueDescription) || vm.IssueDescription.Trim().Length < 10)
            ModelState.AddModelError(nameof(vm.IssueDescription), "Mô tả lỗi tối thiểu 10 ký tự.");

        var eligible = await IsEligibleAsync(customerId, vm.OrderId, vm.ComponentId, ct);
        if (!eligible)
            ModelState.AddModelError("", "Đơn/sản phẩm không đủ điều kiện bảo hành (cần đơn đã giao / hoàn thành).");

        if (!ModelState.IsValid)
        {
            await FillEligibleItemsAsync(vm, customerId, ct);
            return View(vm);
        }

        _db.WarrantyClaims.Add(new WarrantyClaimEntity
        {
            OrderId = vm.OrderId,
            ComponentId = vm.ComponentId,
            CustomerId = customerId,
            SerialNumber = string.IsNullOrWhiteSpace(vm.SerialNumber) ? null : vm.SerialNumber.Trim(),
            IssueDescription = vm.IssueDescription.Trim(),
            Status = WarrantyClaimStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        TempData["ProfileInfo"] = "Đã gửi yêu cầu bảo hành. Admin sẽ xử lý sớm.";
        return RedirectToAction(nameof(Index));
    }

    private async Task FillEligibleItemsAsync(CreateWarrantyViewModel vm, int customerId, CancellationToken ct)
    {
        var items = await (
            from o in _db.Orders.AsNoTracking()
            join oi in _db.OrderItems.AsNoTracking() on o.OrderId equals oi.OrderId
            join c in _db.Components.AsNoTracking() on oi.ComponentId equals c.ComponentId
            where o.CustomerId == customerId
            select new { o.OrderId, o.StatusCode, o.CreatedAtUtc, c.ComponentId, c.Name, c.Sku }
        ).ToListAsync(ct);

        vm.EligibleOptions = items
            .Where(x => LoyaltyEligibleStatus(x.StatusCode))
            .Select(x => new SelectListItem(
                $"#{x.OrderId} · {x.Name} ({x.Sku})",
                $"{x.OrderId}|{x.ComponentId}"))
            .DistinctBy(x => x.Value)
            .ToList();
    }

    private async Task<bool> IsEligibleAsync(int customerId, int orderId, int componentId, CancellationToken ct)
    {
        var order = await _db.Orders.AsNoTracking()
            .Where(x => x.OrderId == orderId && x.CustomerId == customerId)
            .Select(x => new { x.StatusCode })
            .FirstOrDefaultAsync(ct);
        if (order is null || !LoyaltyEligibleStatus(order.StatusCode))
            return false;

        return await _db.OrderItems.AnyAsync(
            x => x.OrderId == orderId && x.ComponentId == componentId, ct);
    }

    private static bool LoyaltyEligibleStatus(string? statusCode)
    {
        var n = OrderStatusCatalog.Normalize(statusCode);
        return n is OrderStatusCatalog.PaidCompleted
            or OrderStatusCatalog.DeliveredCollected
            or OrderStatusCatalog.DeliveredUnpaid
            or OrderStatusCatalog.Paid;
    }

    private bool TryGetCustomerId(out int customerId)
    {
        customerId = 0;
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out customerId);
    }
}

public sealed class CustomerWarrantyRow
{
    public int WarrantyClaimId { get; set; }
    public int OrderId { get; set; }
    public string ProductName { get; set; } = "";
    public string Sku { get; set; } = "";
    public string? SerialNumber { get; set; }
    public string IssueDescription { get; set; } = "";
    public string Status { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
    public string StatusBadgeClass { get; set; } = "";
    public string? AdminNote { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}

public sealed class CreateWarrantyViewModel
{
    public int OrderId { get; set; }
    public int ComponentId { get; set; }
    /// <summary>Binding helper: "orderId|componentId"</summary>
    public string? SelectedItem { get; set; }
    public string? SerialNumber { get; set; }
    public string IssueDescription { get; set; } = "";
    public List<SelectListItem> EligibleOptions { get; set; } = [];
}
