using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Domain.Catalog;
using ban_link_kien_PC.Infrastructure;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Controllers;

public sealed class CartController : Controller
{
    private readonly CartManager _cart;
    private readonly PcStoreDbContext _db;

    public CartController(CartManager cart, PcStoreDbContext db)
    {
        _cart = cart;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var cartKey = CartKeyResolver.GetExistingCartKey(HttpContext);
        var model = new CartIndexViewModel { Cards = [], GrandTotalVnd = 0 };
        ViewBag.CheckoutError = TempData["CheckoutError"] as string;
        ViewBag.CurrentReturnUrl = $"{Request.Path}{Request.QueryString}";

        if (string.IsNullOrEmpty(cartKey))
            return View(model);

        var cards = _cart.GetCards(cartKey);
        if (cards.Count == 0)
            return View(model);

        var ids = cards.SelectMany(x => x.Items).Select(x => x.ComponentId).Distinct().ToList();
        var components = await _db.Components.AsNoTracking()
            .Where(c => ids.Contains(c.ComponentId))
            .ToListAsync(ct);
        var byId = components.ToDictionary(c => c.ComponentId, c => c);
        var categoryMap = await _db.ComponentCategories.AsNoTracking()
            .ToDictionaryAsync(x => x.ComponentCategoryId, x => x.Code, ct);

        foreach (var card in cards)
        {
            var vmCard = new CartCardViewModel
            {
                CardId = card.CardId,
                CardType = card.CardType,
                Title = card.Title,
                Items = []
            };

            foreach (var item in card.Items)
            {
                if (!byId.TryGetValue(item.ComponentId, out var c))
                    continue;

                var categoryCode = categoryMap.TryGetValue(c.ComponentCategoryId, out var code) ? code : string.Empty;
                var isOutStock = !c.IsActive || c.StockQty <= 0 || c.StockQty < item.Qty;
                var lineTotal = isOutStock ? 0 : c.PriceVnd * item.Qty;

                vmCard.Items.Add(new CartCardItemViewModel
                {
                    ComponentId = c.ComponentId,
                    Name = c.Name,
                    Sku = c.Sku,
                    UnitPriceVnd = c.PriceVnd,
                    Qty = item.Qty,
                    LineTotalVnd = lineTotal,
                    IsOutOfStock = isOutStock,
                    StockQty = c.StockQty,
                    CategoryCode = categoryCode,
                    ImageUrl = CatalogImageResolver.Resolve(c.ComponentId, categoryCode, c.Sku, c.ImageUrl)
                });
            }

            if (vmCard.Items.Count == 0)
                continue;

            vmCard.IsOutOfStock = vmCard.Items.Any(x => x.IsOutOfStock);
            vmCard.CardTotalVnd = vmCard.Items.Sum(x => x.LineTotalVnd);
            vmCard.IsSelectable = !vmCard.IsOutOfStock && vmCard.CardTotalVnd > 0;

            if (string.IsNullOrWhiteSpace(vmCard.Title))
            {
                vmCard.Title = vmCard.CardType switch
                {
                    "BUILD_PC" => "Cấu hình PC tự build",
                    "PREBUILT_PC" => "PC lắp sẵn",
                    "WORKSTATION_PC" => "PC Workstation",
                    _ => vmCard.Items[0].Name
                };
            }

            model.Cards.Add(vmCard);
            if (vmCard.IsSelectable)
                model.GrandTotalVnd += vmCard.CardTotalVnd;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int componentId, int qty = 1, string? returnSku = null, CancellationToken ct = default)
    {
        var result = await TryAddToCartAsync(componentId, qty, buyNow: false, ct);
        if (!result.Ok)
        {
            TempData["CheckoutError"] = result.Error;
            return RedirectToDetail(componentId, returnSku);
        }

        TempData["CartSuccess"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>AJAX: thêm vào giỏ, trả JSON (toast trên trang chi tiết).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAjax(int componentId, int qty = 1, CancellationToken ct = default)
    {
        var result = await TryAddToCartAsync(componentId, qty, buyNow: false, ct);
        if (!result.Ok)
            return BadRequest(new { ok = false, error = result.Error ?? "Số lượng trong kho không đủ." });

        return Json(new { ok = true, message = result.Message, cartItemCount = result.CartItemCount });
    }

    /// <summary>Thêm vào giỏ rồi chuyển thẳng Checkout với card vừa thêm.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BuyNow(int componentId, int qty = 1, string? returnSku = null, CancellationToken ct = default)
    {
        var result = await TryAddToCartAsync(componentId, qty, buyNow: true, ct);
        if (!result.Ok)
        {
            TempData["CheckoutError"] = result.Error;
            return RedirectToDetail(componentId, returnSku);
        }

        return RedirectToAction("Index", "Checkout");
    }

    private IActionResult RedirectToDetail(int componentId, string? returnSku, string? productName = null)
    {
        if (!string.IsNullOrWhiteSpace(productName))
            return RedirectToRoute("ProductDetail", new { slug = SeoSlug.From(productName), id = componentId });
        if (!string.IsNullOrWhiteSpace(returnSku))
            return RedirectToAction("Detail", "Catalog", new { sku = returnSku });
        return RedirectToAction("Detail", "Catalog", new { id = componentId });
    }

    private async Task<CartAddResult> TryAddToCartAsync(
        int componentId,
        int qty,
        bool buyNow,
        CancellationToken ct)
    {
        var row = await (from c in _db.Components.AsNoTracking()
                         join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
                         where c.ComponentId == componentId && c.IsActive
                         select new { c.ComponentId, c.Name, c.StockQty, CategoryCode = cat.Code })
            .SingleOrDefaultAsync(ct);

        if (row is null)
            return CartAddResult.Fail("Sản phẩm không tồn tại hoặc đã ngừng bán.");

        if (row.StockQty <= 0)
            return CartAddResult.Fail("Sản phẩm đã hết hàng — không thể thêm vào giỏ.");

        var requestQty = Math.Max(1, qty);
        var cartKey = CartKeyResolver.GetOrCreateCartKey(HttpContext);

        var existingQty = 0;
        if (!buyNow)
        {
            existingQty = _cart.GetCards(cartKey)
                .SelectMany(c => c.Items)
                .Where(i => i.ComponentId == componentId)
                .Sum(i => i.Qty);
        }

        var targetQty = buyNow ? requestQty : existingQty + requestQty;
        if (targetQty > row.StockQty)
            return CartAddResult.Fail(
                $"{InventoryStockService.InsufficientStockMessage} (còn {row.StockQty}, bạn đang chọn {targetQty}).");

        var isPrebuilt = string.Equals(row.CategoryCode, "PC_PREBUILT", StringComparison.OrdinalIgnoreCase);
        var isWorkstation = string.Equals(row.CategoryCode, "PC_WORKSTATION", StringComparison.OrdinalIgnoreCase);
        var cardType = isPrebuilt ? "PREBUILT_PC" : (isWorkstation ? "WORKSTATION_PC" : "COMPONENT");
        var asSeparateCard = isPrebuilt || isWorkstation;

        var cardId = _cart.AddComponentCard(
            cartKey, row.ComponentId, targetQty, cardType, row.Name, asSeparateCard);

        if (buyNow)
            _cart.SetCheckoutSelection(cartKey, [cardId]);

        var itemCount = _cart.GetCards(cartKey).SelectMany(c => c.Items).Sum(i => i.Qty);
        var msg = buyNow
            ? "Đã thêm vào giỏ — chuyển sang thanh toán."
            : $"Đã thêm «{row.Name}» × {requestQty} vào giỏ hàng.";
        return CartAddResult.Success(msg, itemCount);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult PrepareCheckout([FromForm] List<string> selectedCardIds)
    {
        var cartKey = CartKeyResolver.GetExistingCartKey(HttpContext);
        if (string.IsNullOrEmpty(cartKey))
        {
            TempData["CheckoutError"] = "Giỏ hàng đang trống, chưa thể thanh toán.";
            return RedirectToAction(nameof(Index));
        }

        _cart.SetCheckoutSelection(cartKey, selectedCardIds ?? []);
        if (_cart.GetCheckoutSelection(cartKey).Count == 0)
        {
            TempData["CheckoutError"] = "Vui lòng chọn ít nhất 1 card hợp lệ để thanh toán.";
            return RedirectToAction(nameof(Index));
        }

        return RedirectToAction("Index", "Checkout");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveCard(string cardId)
    {
        var cartKey = CartKeyResolver.GetExistingCartKey(HttpContext);
        if (!string.IsNullOrEmpty(cartKey))
            _cart.RemoveCard(cartKey, cardId);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Remove(int componentId)
    {
        var cartKey = CartKeyResolver.GetExistingCartKey(HttpContext);
        if (!string.IsNullOrEmpty(cartKey))
            _cart.Remove(cartKey, componentId);
        return RedirectToAction(nameof(Index));
    }
}

public sealed class CartIndexViewModel
{
    public List<CartCardViewModel> Cards { get; set; } = [];
    public decimal GrandTotalVnd { get; set; }
}

public sealed class CartCardViewModel
{
    public string CardId { get; set; } = "";
    public string CardType { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsOutOfStock { get; set; }
    public bool IsSelectable { get; set; }
    public decimal CardTotalVnd { get; set; }
    public List<CartCardItemViewModel> Items { get; set; } = [];
}

public sealed class CartCardItemViewModel
{
    public int ComponentId { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public string CategoryCode { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public int StockQty { get; set; }
    public bool IsOutOfStock { get; set; }
    public decimal UnitPriceVnd { get; set; }
    public int Qty { get; set; }
    public decimal LineTotalVnd { get; set; }
}

internal sealed record CartAddResult(bool Ok, string? Error, string? Message, int CartItemCount)
{
    public static CartAddResult Fail(string error) => new(false, error, null, 0);
    public static CartAddResult Success(string message, int count) => new(true, null, message, count);
}
