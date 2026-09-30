using ban_link_kien_PC.Domain.Auth;
using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Controllers;

/// <summary>Dashboard Nhân viên Kỹ thuật — cập nhật tiến độ lắp ráp PC.</summary>
[Authorize]
public sealed class TechnicalController : Controller
{
    private readonly PcStoreDbContext _db;
    private readonly OrderAssemblyService _assembly;

    public TechnicalController(PcStoreDbContext db, OrderAssemblyService assembly)
    {
        _db = db;
        _assembly = assembly;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? tab, string? keyword, CancellationToken ct)
    {
        var guard = EnsureTechnicalAccess();
        if (guard is not null) return guard;

        var activeTab = NormalizeTab(tab);
        var q = _db.Orders.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            q = q.Where(x =>
                x.ReceiverName.Contains(k) ||
                x.ReceiverPhone.Contains(k) ||
                x.OrderId.ToString().Contains(k));
        }

        var baseStatuses = await q
            .Select(x => x.StatusCode)
            .ToListAsync(ct);

        var counts = new TechnicalTabCounts
        {
            Exported = baseStatuses.Count(s =>
                OrderStatusCatalog.Normalize(s) == OrderStatusCatalog.Exported),
            Assembling = baseStatuses.Count(s =>
                OrderStatusCatalog.Normalize(s) == OrderStatusCatalog.Assembling),
            Ready = baseStatuses.Count(s =>
                OrderStatusCatalog.Normalize(s) == OrderStatusCatalog.ReadyToDeliver),
            Issues = baseStatuses.Count(s =>
            {
                var n = OrderStatusCatalog.Normalize(s);
                return n is OrderStatusCatalog.IssueDoa or OrderStatusCatalog.IssueCaseMismatch;
            })
        };

        q = activeTab switch
        {
            "assembling" => q.Where(x => x.StatusCode == OrderStatusCatalog.Assembling),
            "ready" => q.Where(x => x.StatusCode == OrderStatusCatalog.ReadyToDeliver),
            "issues" => q.Where(x =>
                x.StatusCode == OrderStatusCatalog.IssueDoa
                || x.StatusCode == OrderStatusCatalog.IssueCaseMismatch),
            // Mặc định: Đã xuất kho (cần bắt đầu ráp)
            _ => q.Where(x => x.StatusCode == OrderStatusCatalog.Exported)
        };

        var rows = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(200)
            .Select(x => new TechnicalOrderRow
            {
                OrderId = x.OrderId,
                ReceiverName = x.ReceiverName,
                ReceiverPhone = x.ReceiverPhone,
                ShippingAddress = x.ShippingAddress,
                StatusCode = x.StatusCode,
                TotalPriceVnd = x.TotalPriceVnd,
                Note = x.Note,
                OrderType = x.OrderType,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(ct);

        var orderIds = rows.Select(r => r.OrderId).ToList();
        var itemCounts = await _db.OrderItems.AsNoTracking()
            .Where(i => orderIds.Contains(i.OrderId))
            .GroupBy(i => i.OrderId)
            .Select(g => new { OrderId = g.Key, Count = g.Count(), Qty = g.Sum(x => x.Qty) })
            .ToDictionaryAsync(x => x.OrderId, ct);

        var latestNotes = await _db.OrderTimelines.AsNoTracking()
            .Where(t => orderIds.Contains(t.OrderId))
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new { t.OrderId, t.Note, t.IssueReasonCode, t.CreatedAtUtc })
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            row.StatusCode = OrderStatusCatalog.Normalize(row.StatusCode);
            row.StatusDisplay = OrderStatusCatalog.ToDisplayName(row.StatusCode);
            row.StatusBadgeClass = OrderStatusCatalog.ToBadgeClass(row.StatusCode);
            if (itemCounts.TryGetValue(row.OrderId, out var ic))
            {
                row.LineCount = ic.Count;
                row.TotalQty = ic.Qty;
            }

            var last = latestNotes.FirstOrDefault(n => n.OrderId == row.OrderId);
            if (last is not null)
            {
                row.LatestTimelineNote = last.Note;
                row.IssueReasonDisplay = string.IsNullOrWhiteSpace(last.IssueReasonCode)
                    ? null
                    : OrderAssemblyService.IssueReasonDisplay(last.IssueReasonCode);
            }
        }

        return View(new TechnicalDashboardViewModel
        {
            Tab = activeTab,
            Keyword = keyword,
            Counts = counts,
            Rows = rows
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartAssembling(int orderId, string? tab, string? keyword, CancellationToken ct)
    {
        var guard = EnsureTechnicalAccess();
        if (guard is not null) return guard;

        var result = await _assembly.StartAssemblingAsync(orderId, ActorName(), ct);
        TempData[result.Ok ? "TechInfo" : "TechError"] = result.Message;
        return RedirectToAction(nameof(Index), new { tab = tab ?? "exported", keyword });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteAssembly(int orderId, string? tab, string? keyword, CancellationToken ct)
    {
        var guard = EnsureTechnicalAccess();
        if (guard is not null) return guard;

        var result = await _assembly.CompleteAssemblyAsync(orderId, ActorName(), ct);
        TempData[result.Ok ? "TechInfo" : "TechError"] = result.Message;
        return RedirectToAction(nameof(Index), new { tab = result.Ok ? "ready" : (tab ?? "assembling"), keyword });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReportIssue(
        int orderId,
        string issueReasonCode,
        string? note,
        string? tab,
        string? keyword,
        CancellationToken ct)
    {
        var guard = EnsureTechnicalAccess();
        if (guard is not null) return guard;

        var result = await _assembly.ReportIssueAsync(orderId, issueReasonCode, note, ActorName(), ct);
        TempData[result.Ok ? "TechInfo" : "TechError"] = result.Message;
        return RedirectToAction(nameof(Index), new { tab = result.Ok ? "issues" : (tab ?? "exported"), keyword });
    }

    /// <summary>Kho/Admin xuất kho sang kỹ thuật (từ APPROVED/PACKED hoặc sau khi đổi linh kiện lỗi).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkExported(int orderId, string? tab, string? keyword, CancellationToken ct)
    {
        var guard = EnsureTechnicalAccess();
        if (guard is not null) return guard;

        var result = await _assembly.MarkExportedAsync(orderId, ActorName(), ct);
        TempData[result.Ok ? "TechInfo" : "TechError"] = result.Message;
        return RedirectToAction(nameof(Index), new { tab = "exported", keyword });
    }

    private string ActorName() => User.Identity?.Name ?? "technical";

    private static string NormalizeTab(string? tab)
    {
        var t = (tab ?? "exported").Trim().ToLowerInvariant();
        return t is "assembling" or "ready" or "issues" ? t : "exported";
    }

    /// <summary>Cho phép SuperAdmin hoặc Technical vào dashboard kỹ thuật.</summary>
    private IActionResult? EnsureTechnicalAccess()
    {
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Index), "Technical") });

        if (!StaffRoles.IsInAnyRole(User, StaffRoles.Technical, StaffRoles.Warehouse, StaffRoles.SuperAdmin))
            return Forbid();

        return null;
    }
}

public sealed class TechnicalDashboardViewModel
{
    public string Tab { get; set; } = "exported";
    public string? Keyword { get; set; }
    public TechnicalTabCounts Counts { get; set; } = new();
    public List<TechnicalOrderRow> Rows { get; set; } = [];
}

public sealed class TechnicalTabCounts
{
    public int Exported { get; set; }
    public int Assembling { get; set; }
    public int Ready { get; set; }
    public int Issues { get; set; }
}

public sealed class TechnicalOrderRow
{
    public int OrderId { get; set; }
    public string ReceiverName { get; set; } = "";
    public string ReceiverPhone { get; set; } = "";
    public string ShippingAddress { get; set; } = "";
    public string StatusCode { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
    public string StatusBadgeClass { get; set; } = "text-bg-light";
    public decimal TotalPriceVnd { get; set; }
    public string? Note { get; set; }
    public string OrderType { get; set; } = "ONLINE";
    public DateTime CreatedAtUtc { get; set; }
    public int LineCount { get; set; }
    public int TotalQty { get; set; }
    public string? LatestTimelineNote { get; set; }
    public string? IssueReasonDisplay { get; set; }
}
