using ban_link_kien_PC.Domain.Catalog;
using ban_link_kien_PC.Domain.Notifications;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ban_link_kien_PC.Controllers;

public sealed class CatalogController : Controller
{
    private readonly CatalogFilterFacade _catalogFilterFacade;
    private readonly ProductDetailFacade _productDetailFacade;
    private readonly IStockWaitlistService _stockWaitlist;
    private readonly IUserNotificationCenter _notificationCenter;
    private readonly PcStoreDbContext _db;

    public CatalogController(
        CatalogFilterFacade catalogFilterFacade,
        ProductDetailFacade productDetailFacade,
        IStockWaitlistService stockWaitlist,
        IUserNotificationCenter notificationCenter,
        PcStoreDbContext db)
    {
        _catalogFilterFacade = catalogFilterFacade;
        _productDetailFacade = productDetailFacade;
        _stockWaitlist = stockWaitlist;
        _notificationCenter = notificationCenter;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] CatalogFilterInput input, CancellationToken ct)
    {
        var vm = await _catalogFilterFacade.BuildPageAsync(input, ct);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Detail(int? id, string? sku, string? slug, CancellationToken ct)
    {
        if (!id.HasValue && !string.IsNullOrWhiteSpace(sku))
        {
            id = await _catalogFilterFacade.FindComponentIdBySkuAsync(sku, ct);
        }

        if (!id.HasValue)
            return NotFound();

        var vm = await _productDetailFacade.BuildAsync(id.Value, ct);
        if (vm is null)
            return NotFound();

        var expectedSlug = SeoSlug.From(vm.Name);
        var seoPath = SeoSlug.ProductPath(vm.Name, vm.ComponentId);
        var currentPath = Request.Path.Value ?? "";
        if (!string.Equals(currentPath, seoPath, StringComparison.OrdinalIgnoreCase))
            return RedirectPermanent(seoPath);

        // SEO On-page
        ViewData["Title"] = vm.Name;
        var brandPart = string.IsNullOrWhiteSpace(vm.BrandName) ? "" : $" {vm.BrandName}";
        var desc = $"{vm.Name}{brandPart} — {vm.CategoryName}. Giá {vm.PriceVnd:N0} đ tại Tech Arena. PC Gaming & linh kiện chính hãng, bảo hành uy tín.";
        if (desc.Length > 160)
            desc = desc[..157].TrimEnd() + "…";
        ViewData["MetaDescription"] = desc;
        ViewData["MetaKeywords"] = $"{vm.Name}, {vm.Sku}, {vm.BrandName}, {vm.CategoryName}, Tech Arena, linh kiện máy tính, PC Gaming";
        ViewData["OgTitle"] = vm.Name;
        ViewData["OgDescription"] = desc;
        ViewData["OgImage"] = vm.ImageUrl;
        ViewData["OgType"] = "product";
        ViewData["CanonicalUrl"] = Url.RouteUrl("ProductDetail", new { slug = expectedSlug, id = vm.ComponentId });

        ViewBag.WaitlistInfo = TempData["WaitlistInfo"] as string;
        ViewBag.WaitlistError = TempData["WaitlistError"] as string;
        ViewBag.IsFollowingWaitlist = false;
        if (User.Identity?.IsAuthenticated == true)
        {
            var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userIdRaw, out var customerId))
                ViewBag.IsFollowingWaitlist = await _stockWaitlist.IsInWaitlistAsync(vm.ComponentId, customerId, ct);
        }

        return View(vm);
    }

    private IActionResult RedirectToProduct(int componentId, string? productName = null, string? returnSku = null)
    {
        if (!string.IsNullOrWhiteSpace(productName))
            return RedirectToRoute("ProductDetail", new { slug = SeoSlug.From(productName), id = componentId });

        if (!string.IsNullOrWhiteSpace(returnSku))
            return RedirectToAction(nameof(Detail), new { sku = returnSku });

        return RedirectToAction(nameof(Detail), new { id = componentId });
    }

    [HttpGet]
    public async Task<IActionResult> Suggest([FromQuery] string? keyword, [FromQuery] string? categoryCode, CancellationToken ct)
    {
        var items = await _catalogFilterFacade.SuggestAsync(keyword, categoryCode, 8, ct);
        return Ok(items);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> JoinWaitlist(int componentId, string? returnSku, string? returnUrl, CancellationToken ct)
    {
        var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdRaw, out var customerId))
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Detail), new { id = componentId }) });

        var customerExists = await _db.Customers.AsNoTracking()
            .AnyAsync(x => x.CustomerId == customerId, ct);
        if (!customerExists)
        {
            TempData["WaitlistError"] = "Phiên đăng nhập không còn hợp lệ với dữ liệu hiện tại. Vui lòng đăng xuất và đăng nhập lại.";
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToProduct(componentId, returnSku: returnSku);
        }

        var component = await _db.Components.AsNoTracking()
            .Where(x => x.ComponentId == componentId)
            .Select(x => new { x.ComponentId, x.Name })
            .FirstOrDefaultAsync(ct);
        if (component is null)
        {
            TempData["WaitlistError"] = "Không tìm thấy sản phẩm để đăng ký theo dõi.";
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await _stockWaitlist.AddToWaitlistAsync(componentId, customerId, ct);
            TempData["WaitlistInfo"] = "Đã đăng ký theo dõi. Chúng tôi sẽ thông báo khi có hàng.";
        }
        catch (DbUpdateException)
        {
            TempData["WaitlistError"] = "Không thể đăng ký theo dõi lúc này do dữ liệu chưa đồng bộ. Vui lòng thử lại sau.";
        }
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToProduct(component.ComponentId, component.Name, returnSku);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LeaveWaitlist(int componentId, string? returnSku, CancellationToken ct)
    {
        var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdRaw, out var customerId))
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Detail), new { id = componentId }) });

        var removed = await _stockWaitlist.RemoveFromWaitlistAsync(componentId, customerId, ct);
        TempData["WaitlistInfo"] = removed
            ? "Đã hủy theo dõi sản phẩm."
            : "Bạn chưa theo dõi sản phẩm này.";

        var name = await _db.Components.AsNoTracking()
            .Where(x => x.ComponentId == componentId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(ct);
        return RedirectToProduct(componentId, name, returnSku);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ClearNotifications(bool clearAll = false, string? returnUrl = null)
    {
        var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdRaw, out var customerId))
            return RedirectToAction("Login", "Account");

        if (clearAll)
            _notificationCenter.ClearAll(customerId);

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    [HttpGet]
    [ActionName("ClearNotifications")]
    public IActionResult ClearNotificationsGet(string? returnUrl = null)
    {
        var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(userIdRaw, out var customerId))
            _notificationCenter.ClearAll(customerId);

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        var referer = Request.Headers.Referer.ToString();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
        {
            var localTarget = refererUri.PathAndQuery;
            if (Url.IsLocalUrl(localTarget))
                return Redirect(localTarget);
        }

        return RedirectToAction("Index", "Home");
    }
}
