using ban_link_kien_PC.Domain.Auth;
using ban_link_kien_PC.Domain.Catalog;
using ban_link_kien_PC.Domain.Notifications;
using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Domain.Shipping;
using ban_link_kien_PC.Infrastructure.Persistence;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Controllers;

[Authorize]
public sealed class AdminController : Controller
{
    private readonly PcStoreDbContext _db;
    private readonly IStockWaitlistService _waitlist;
    private readonly IUserNotificationCenter _notifications;
    private readonly InventoryStockService _stock;
    private readonly Domain.Customers.LoyaltyService _loyalty;
    private readonly OrderFulfillmentService _fulfillment;
    private readonly CreateShipmentService _shipments;
    private readonly OrderAssemblyService _assembly;
    private readonly OrderCancellationService _cancellation;
    private readonly OrderTimelineService _timeline;
    private readonly RevenueReportService _revenue;
    private readonly IWebHostEnvironment _env;

    public AdminController(
        PcStoreDbContext db,
        IStockWaitlistService waitlist,
        IUserNotificationCenter notifications,
        InventoryStockService stock,
        Domain.Customers.LoyaltyService loyalty,
        OrderFulfillmentService fulfillment,
        CreateShipmentService shipments,
        OrderAssemblyService assembly,
        OrderCancellationService cancellation,
        OrderTimelineService timeline,
        RevenueReportService revenue,
        IWebHostEnvironment env)
    {
        _db = db;
        _waitlist = waitlist;
        _notifications = notifications;
        _stock = stock;
        _loyalty = loyalty;
        _fulfillment = fulfillment;
        _shipments = shipments;
        _assembly = assembly;
        _cancellation = cancellation;
        _timeline = timeline;
        _revenue = revenue;
        _env = env;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var totalProducts = await _db.Components.CountAsync(ct);
        var activeProducts = await _db.Components.CountAsync(x => x.IsActive, ct);
        var outOfStock = await _db.Components.CountAsync(x => x.StockQty <= 0, ct);
        var lowStock = await _db.Components.CountAsync(x => x.StockQty > 0 && x.StockQty <= 5, ct);
        var waitlistRows = await _db.StockWaitlists.CountAsync(ct);
        var totalOrders = await _db.Orders.CountAsync(ct);

        var rawStatusCounts = await _db.Orders.AsNoTracking()
            .GroupBy(x => x.StatusCode)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var orderByStatus = rawStatusCounts
            .Select(x => new AdminStatusCountRow(OrderStatusCatalog.ToDisplayName(x.Key), x.Count))
            .OrderByDescending(x => x.Count)
            .ToList();

        var vm = new AdminDashboardViewModel
        {
            TotalProducts = totalProducts,
            ActiveProducts = activeProducts,
            OutOfStockProducts = outOfStock,
            LowStockProducts = lowStock,
            WaitlistRows = waitlistRows,
            TotalOrders = totalOrders,
            OrderStatusRows = orderByStatus
        };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Revenue(
        DateTime? fromDate,
        DateTime? toDate,
        string? statusCode,
        string? payment,
        CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        // Mặc định: tháng hiện tại (VN)
        if (!fromDate.HasValue && !toDate.HasValue)
        {
            var tz = ResolveVietnamTimeZone();
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            fromDate = new DateTime(nowLocal.Year, nowLocal.Month, 1);
            toDate = nowLocal.Date;
        }

        var report = await _revenue.BuildAsync(fromDate, toDate, statusCode, payment, ct);

        var statusOptions = new List<SelectListItem>
        {
            new("Tất cả trạng thái", ""),
            new("Chờ xác nhận", OrderStatusCatalog.PendingConfirmation),
            new("Đã duyệt", OrderStatusCatalog.Approved),
            new("Đang giao hàng", OrderStatusCatalog.Shipping),
            new("Chờ thanh toán", OrderStatusCatalog.WaitingPayment),
            new("Đã thanh toán", OrderStatusCatalog.Paid),
            new("Đã thanh toán - Hoàn thành", OrderStatusCatalog.PaidCompleted),
            new("Giao thành công — đã thu tiền", OrderStatusCatalog.DeliveredCollected),
            new("Giao thành công — chưa thu tiền", OrderStatusCatalog.DeliveredUnpaid),
            new("Giao không thành công", OrderStatusCatalog.DeliveryFailed),
            new("Đã hủy", OrderStatusCatalog.Cancelled),
            new("Hủy (lỗi tồn kho)", OrderStatusCatalog.CancelledNoStock)
        };

        return View(new AdminRevenueViewModel
        {
            FromDate = report.FromDate,
            ToDate = report.ToDate,
            StatusCode = report.StatusFilter,
            Payment = report.PaymentFilter,
            StatusOptions = statusOptions,
            Report = report
        });
    }

    [HttpGet]
    public async Task<IActionResult> RevenueExport(
        DateTime? fromDate,
        DateTime? toDate,
        string? statusCode,
        string? payment,
        CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        if (!fromDate.HasValue && !toDate.HasValue)
        {
            var tz = ResolveVietnamTimeZone();
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            fromDate = new DateTime(nowLocal.Year, nowLocal.Month, 1);
            toDate = nowLocal.Date;
        }

        var report = await _revenue.BuildAsync(fromDate, toDate, statusCode, payment, ct);
        var tzExport = ResolveVietnamTimeZone();

        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var ws = workbook.Worksheets.Add("Doanh thu");
        ws.Cell(1, 1).Value = "Mã đơn";
        ws.Cell(1, 2).Value = "Ngày tạo";
        ws.Cell(1, 3).Value = "Khách nhận";
        ws.Cell(1, 4).Value = "SĐT";
        ws.Cell(1, 5).Value = "Thanh toán";
        ws.Cell(1, 6).Value = "Trạng thái";
        ws.Cell(1, 7).Value = "Tổng tiền";
        ws.Cell(1, 8).Value = "Giảm giá";
        ws.Cell(1, 9).Value = "Tính DT?";
        var header = ws.Range(1, 1, 1, 9);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#0F766E");
        header.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

        var row = 2;
        foreach (var o in report.Orders)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(o.CreatedAtUtc, DateTimeKind.Utc), tzExport);
            ws.Cell(row, 1).Value = o.OrderId;
            ws.Cell(row, 2).Value = local;
            ws.Cell(row, 2).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            ws.Cell(row, 3).Value = o.ReceiverName;
            ws.Cell(row, 4).Value = o.ReceiverPhone;
            ws.Cell(row, 5).Value = o.PaymentMethodCode;
            ws.Cell(row, 6).Value = o.StatusDisplay;
            ws.Cell(row, 7).Value = o.TotalPriceVnd;
            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 8).Value = o.DiscountAmountVnd;
            ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 9).Value = o.IsRevenue ? "Có" : "Không";
            row++;
        }

        if (report.Orders.Count > 0)
        {
            ws.Cell(row, 6).Value = "Tổng";
            ws.Cell(row, 6).Style.Font.Bold = true;
            ws.Cell(row, 7).FormulaA1 = $"SUM(G2:G{row - 1})";
            ws.Cell(row, 7).Style.Font.Bold = true;
            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
        }

        var ws2 = workbook.Worksheets.Add("Theo trạng thái");
        ws2.Cell(1, 1).Value = "Trạng thái";
        ws2.Cell(1, 2).Value = "Số đơn";
        ws2.Cell(1, 3).Value = "Tổng giá trị";
        ws2.Cell(1, 4).Value = "Doanh thu ghi nhận";
        ws2.Range(1, 1, 1, 4).Style.Font.Bold = true;
        var r2 = 2;
        foreach (var b in report.ByStatus)
        {
            ws2.Cell(r2, 1).Value = b.StatusDisplay;
            ws2.Cell(r2, 2).Value = b.OrderCount;
            ws2.Cell(r2, 3).Value = b.TotalAmountVnd;
            ws2.Cell(r2, 3).Style.NumberFormat.Format = "#,##0";
            ws2.Cell(r2, 4).Value = b.RevenueAmountVnd;
            ws2.Cell(r2, 4).Style.NumberFormat.Format = "#,##0";
            r2++;
        }

        ws.Columns().AdjustToContents();
        ws2.Columns().AdjustToContents();

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        var fileName = $"DoanhThu_{(fromDate ?? DateTime.Today):yyyyMMdd}_{(toDate ?? DateTime.Today):yyyyMMdd}.xlsx";
        return File(
            stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    [HttpGet]
    public async Task<IActionResult> Products(string? keyword, string? group, string? categoryCode, string? status, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var q = _db.Components.AsNoTracking().Include(x => x.Category).AsQueryable();
        var normalizedGroup = (group ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedCategoryCode = (categoryCode ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedStatus = (status ?? string.Empty).Trim().ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(normalizedCategoryCode))
        {
            q = q.Where(x => x.Category != null && x.Category.Code == normalizedCategoryCode);
        }
        else if (normalizedGroup == "PREBUILT")
        {
            q = q.Where(x => x.Category != null && x.Category.Code == "PC_PREBUILT");
        }
        else if (normalizedGroup == "WORKSTATION")
        {
            q = q.Where(x => x.Category != null && x.Category.Code == "PC_WORKSTATION");
        }
        else if (normalizedGroup == "COMPONENT")
        {
            q = q.Where(x => x.Category != null
                && x.Category.Code != "PC_PREBUILT"
                && x.Category.Code != "PC_WORKSTATION");
        }

        if (normalizedStatus == "ACTIVE")
            q = q.Where(x => x.IsActive);
        else if (normalizedStatus == "INACTIVE")
            q = q.Where(x => !x.IsActive);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            q = q.Where(x => x.Name.Contains(k) || x.Sku.Contains(k));
        }

        var rowsRaw = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(300)
            .Select(x => new
            {
                x.ComponentId,
                x.Sku,
                x.Name,
                CategoryName = x.Category != null ? x.Category.DisplayName : "N/A",
                CategoryCode = x.Category != null ? x.Category.Code : "",
                x.PriceVnd,
                x.StockQty,
                x.IsActive,
                x.ImageUrl
            })
            .ToListAsync(ct);

        var rows = rowsRaw.Select(x => new AdminProductRow
        {
            ComponentId = x.ComponentId,
            Sku = x.Sku,
            Name = x.Name,
            CategoryName = x.CategoryName,
            CategoryCode = x.CategoryCode,
            PriceVnd = x.PriceVnd,
            StockQty = x.StockQty,
            IsActive = x.IsActive,
            ImageUrl = CatalogImageResolver.Resolve(x.ComponentId, x.CategoryCode, x.Sku, x.ImageUrl)
        }).ToList();

        var categoryOptions = await _db.ComponentCategories.AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .Select(x => new SelectListItem(x.DisplayName, x.Code))
            .ToListAsync(ct);

        var countsRaw = await _db.Components.AsNoTracking()
            .Join(_db.ComponentCategories.AsNoTracking(),
                c => c.ComponentCategoryId,
                cat => cat.ComponentCategoryId,
                (c, cat) => new { cat.Code, cat.DisplayName })
            .GroupBy(x => new { x.Code, x.DisplayName })
            .Select(g => new { g.Key.Code, g.Key.DisplayName, Count = g.Count() })
            .OrderBy(x => x.DisplayName)
            .ToListAsync(ct);

        var counts = countsRaw.Select(x => new AdminCategoryCountRow
        {
            CategoryCode = x.Code,
            CategoryName = x.DisplayName,
            Count = x.Count
        }).ToList();

        var componentCount = counts
            .Where(x => x.CategoryCode != "PC_PREBUILT" && x.CategoryCode != "PC_WORKSTATION")
            .Sum(x => x.Count);
        var prebuiltCount = counts.FirstOrDefault(x => x.CategoryCode == "PC_PREBUILT")?.Count ?? 0;
        var workstationCount = counts.FirstOrDefault(x => x.CategoryCode == "PC_WORKSTATION")?.Count ?? 0;
        var totalCount = counts.Sum(x => x.Count);

        return View(new AdminProductsViewModel
        {
            Keyword = keyword,
            Group = normalizedGroup,
            CategoryCode = normalizedCategoryCode,
            Status = normalizedStatus,
            Rows = rows,
            CategoryOptions = categoryOptions,
            CategoryCounts = counts,
            TotalCount = totalCount,
            ComponentCount = componentCount,
            PrebuiltCount = prebuiltCount,
            WorkstationCount = workstationCount
        });
    }

    [HttpGet]
    public async Task<IActionResult> ProductCreate(CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var vm = new AdminProductEditViewModel();
        await FillProductEditOptionsAsync(vm, ct);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> ProductCreate(AdminProductEditViewModel vm, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        ValidateProductForm(vm);
        if (!ModelState.IsValid)
        {
            await FillProductEditOptionsAsync(vm, ct);
            return View(vm);
        }

        var skuExists = await _db.Components.AnyAsync(x => x.Sku == vm.Sku.Trim(), ct);
        if (skuExists)
        {
            ModelState.AddModelError(nameof(vm.Sku), "SKU đã tồn tại.");
            await FillProductEditOptionsAsync(vm, ct);
            return View(vm);
        }

        string? imageUrl = null;
        if (vm.ImageUpload is { Length: > 0 })
        {
            var saved = await ProductImageStorage.SaveAsync(_env, vm.ImageUpload, vm.Sku, ct);
            if (!saved.Ok)
            {
                ModelState.AddModelError(nameof(vm.ImageUpload), saved.Error ?? "Upload ảnh thất bại.");
                await FillProductEditOptionsAsync(vm, ct);
                return View(vm);
            }
            imageUrl = saved.RelativeUrl;
        }

        var entity = new ComponentEntity
        {
            ComponentCategoryId = vm.ComponentCategoryId,
            BrandId = vm.BrandId,
            Sku = vm.Sku.Trim(),
            Name = vm.Name.Trim(),
            PriceVnd = vm.PriceVnd,
            StockQty = Math.Max(0, vm.StockQty),
            IsActive = vm.IsActive,
            IsHot = vm.IsHot,
            IsBestSeller = vm.IsBestSeller,
            ImageUrl = imageUrl,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Components.Add(entity);
        await _db.SaveChangesAsync(ct);

        TempData["AdminInfo"] = "Đã tạo sản phẩm mới.";
        return RedirectToAction(nameof(Products));
    }

    [HttpGet]
    public async Task<IActionResult> ProductEdit(int id, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var x = await _db.Components.AsNoTracking().SingleOrDefaultAsync(x => x.ComponentId == id, ct);
        if (x is null) return NotFound();

        var vm = new AdminProductEditViewModel
        {
            ComponentId = x.ComponentId,
            ComponentCategoryId = x.ComponentCategoryId,
            BrandId = x.BrandId,
            Sku = x.Sku,
            Name = x.Name,
            PriceVnd = x.PriceVnd,
            StockQty = x.StockQty,
            IsActive = x.IsActive,
            IsHot = x.IsHot,
            IsBestSeller = x.IsBestSeller,
            CurrentImageUrl = CatalogImageResolver.Resolve(x.ComponentId, "", x.Sku, x.ImageUrl)
        };
        await FillProductEditOptionsAsync(vm, ct);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> ProductEdit(AdminProductEditViewModel vm, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        ValidateProductForm(vm);
        if (!ModelState.IsValid)
        {
            await FillProductEditOptionsAsync(vm, ct);
            return View(vm);
        }

        var entity = await _db.Components.SingleOrDefaultAsync(x => x.ComponentId == vm.ComponentId, ct);
        if (entity is null) return NotFound();

        var skuExists = await _db.Components.AnyAsync(x => x.ComponentId != vm.ComponentId && x.Sku == vm.Sku.Trim(), ct);
        if (skuExists)
        {
            ModelState.AddModelError(nameof(vm.Sku), "SKU đã tồn tại.");
            await FillProductEditOptionsAsync(vm, ct);
            return View(vm);
        }

        if (vm.ImageUpload is { Length: > 0 })
        {
            var saved = await ProductImageStorage.SaveAsync(_env, vm.ImageUpload, vm.Sku, ct);
            if (!saved.Ok)
            {
                ModelState.AddModelError(nameof(vm.ImageUpload), saved.Error ?? "Upload ảnh thất bại.");
                vm.CurrentImageUrl = CatalogImageResolver.Resolve(entity.ComponentId, "", entity.Sku, entity.ImageUrl);
                await FillProductEditOptionsAsync(vm, ct);
                return View(vm);
            }

            ProductImageStorage.TryDelete(_env, entity.ImageUrl);
            entity.ImageUrl = saved.RelativeUrl;
        }

        entity.ComponentCategoryId = vm.ComponentCategoryId;
        entity.BrandId = vm.BrandId;
        entity.Sku = vm.Sku.Trim();
        entity.Name = vm.Name.Trim();
        entity.PriceVnd = vm.PriceVnd;
        entity.StockQty = Math.Max(0, vm.StockQty);
        entity.IsActive = vm.IsActive;
        entity.IsHot = vm.IsHot;
        entity.IsBestSeller = vm.IsBestSeller;
        await _db.SaveChangesAsync(ct);

        TempData["AdminInfo"] = "Đã cập nhật sản phẩm.";
        return RedirectToAction(nameof(Products));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProductDelete(int id, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var entity = await _db.Components.SingleOrDefaultAsync(x => x.ComponentId == id, ct);
        if (entity is not null)
        {
            var isReferenced =
                await _db.OrderItems.AnyAsync(x => x.ComponentId == id, ct) ||
                await _db.CartItems.AnyAsync(x => x.ComponentId == id, ct) ||
                await _db.StockWaitlists.AnyAsync(x => x.ComponentId == id, ct) ||
                await _db.BuildConfigurationItems.AnyAsync(x => x.ComponentId == id, ct);

            if (isReferenced)
            {
                entity.IsActive = false;
                await _db.SaveChangesAsync(ct);
                TempData["AdminInfo"] = "Sản phẩm đã phát sinh dữ liệu liên quan, hệ thống chuyển sang trạng thái OFF thay vì xóa cứng.";
            }
            else
            {
                _db.Components.Remove(entity);
                await _db.SaveChangesAsync(ct);
                TempData["AdminInfo"] = "Đã xóa sản phẩm.";
            }
        }
        return RedirectToAction(nameof(Products));
    }

    [HttpGet]
    public async Task<IActionResult> Inventory(string? keyword, string? stock, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var stockFilter = (stock ?? string.Empty).Trim().ToLowerInvariant();
        var q = _db.Components.AsNoTracking().Include(x => x.Category).AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            q = q.Where(x => x.Name.Contains(k) || x.Sku.Contains(k));
        }

        if (stockFilter == "out")
            q = q.Where(x => x.StockQty <= 0);
        else if (stockFilter == "low")
            q = q.Where(x => x.StockQty > 0 && x.StockQty <= 5);

        var rows = await q
            .OrderBy(x => x.StockQty)
            .ThenBy(x => x.Name)
            .Take(400)
            .Select(x => new AdminInventoryRow
            {
                ComponentId = x.ComponentId,
                Sku = x.Sku,
                Name = x.Name,
                CategoryName = x.Category != null ? x.Category.DisplayName : "N/A",
                StockQty = x.StockQty,
                IsActive = x.IsActive
            })
            .ToListAsync(ct);

        return View(new AdminInventoryViewModel
        {
            Keyword = keyword,
            StockFilter = stockFilter,
            Rows = rows
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStock(int componentId, int stockQty, bool isActive, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var row = await _db.Components.SingleOrDefaultAsync(x => x.ComponentId == componentId, ct);
        if (row is null) return NotFound();

        var previousStock = row.StockQty;
        row.StockQty = Math.Max(0, stockQty);
        row.IsActive = isActive;
        await _db.SaveChangesAsync(ct);

        // Thông báo ngầm khi có hàng lại (không cần nút demo trên storefront)
        if (previousStock <= 0 && row.StockQty > 0)
        {
            var notified = await _waitlist.ProcessRestockAsync(componentId, row.StockQty, ct);
            if (notified.Count > 0)
            {
                var userIds = await _db.Customers.AsNoTracking()
                    .Where(x => notified.Contains(x.Email))
                    .Select(x => x.CustomerId)
                    .ToListAsync(ct);
                var url = Url.Action("Detail", "Catalog", new { id = componentId });
                foreach (var uid in userIds)
                {
                    _notifications.Push(uid, "Sản phẩm đã có hàng",
                        $"{row.Name} đã có hàng trở lại.", url, null);
                }
            }
        }

        TempData["AdminInfo"] = "Đã cập nhật tồn kho.";
        return RedirectToAction(nameof(Inventory));
    }

    [HttpGet]
    public async Task<IActionResult> Waitlist(CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var rows = await (from w in _db.StockWaitlists.AsNoTracking()
                          join c in _db.Components.AsNoTracking() on w.ComponentId equals c.ComponentId
                          join u in _db.Customers.AsNoTracking() on w.CustomerId equals u.CustomerId
                          join cat in _db.ComponentCategories.AsNoTracking() on c.ComponentCategoryId equals cat.ComponentCategoryId
                          orderby w.CreatedAtUtc descending
                          select new AdminWaitlistRow
                          {
                              StockWaitlistId = w.StockWaitlistId,
                              ComponentId = c.ComponentId,
                              ComponentSku = c.Sku,
                              ComponentName = c.Name,
                              CategoryName = cat.DisplayName,
                              CustomerId = u.CustomerId,
                              CustomerName = u.Username,
                              CustomerEmail = u.Email,
                              CreatedAtUtc = w.CreatedAtUtc
                          }).Take(500).ToListAsync(ct);

        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Orders(
        string? keyword,
        string? tab,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var activeTab = NormalizeOrdersTab(tab);
        var fromUtc = StartOfLocalDayUtc(fromDate);
        var toUtcExclusive = EndOfLocalDayUtcExclusive(toDate);

        var q = _db.Orders.AsNoTracking().AsQueryable();
        if (fromUtc.HasValue)
            q = q.Where(x => x.CreatedAtUtc >= fromUtc.Value);
        if (toUtcExclusive.HasValue)
            q = q.Where(x => x.CreatedAtUtc < toUtcExclusive.Value);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            q = q.Where(x =>
                x.ReceiverName.Contains(k) ||
                x.ReceiverPhone.Contains(k) ||
                x.ShippingAddress.Contains(k) ||
                x.OrderId.ToString().Contains(k));
        }

        // Đếm tab trên cùng bộ lọc ngày/keyword (trước khi lọc tab)
        var baseRows = await q
            .Select(x => new
            {
                x.OrderId,
                x.StatusCode,
                x.PaymentCollectionStatus
            })
            .ToListAsync(ct);

        var counts = new AdminOrdersTabCounts
        {
            All = baseRows.Count,
            Pending = baseRows.Count(x =>
            {
                var s = OrderStatusCatalog.Normalize(x.StatusCode);
                return s is OrderStatusCatalog.PendingConfirmation or OrderStatusCatalog.WaitingPayment;
            }),
            Approved = baseRows.Count(x =>
                OrderStatusCatalog.Normalize(x.StatusCode) == OrderStatusCatalog.Approved),
            Packed = baseRows.Count(x =>
                OrderStatusCatalog.Normalize(x.StatusCode) == OrderStatusCatalog.Packed),
            Shipping = baseRows.Count(x =>
                OrderStatusCatalog.Normalize(x.StatusCode) == OrderStatusCatalog.Shipping),
            Cancelled = baseRows.Count(x =>
                OrderStatusCatalog.IsCancelled(x.StatusCode)),
            Debt = baseRows.Count(x =>
                OrderStatusCatalog.IsDebtOutstanding(x.StatusCode, x.PaymentCollectionStatus)),
            Failed = baseRows.Count(x =>
                OrderStatusCatalog.Normalize(x.StatusCode) == OrderStatusCatalog.DeliveryFailed)
        };

        q = ApplyOrdersTabFilter(q, activeTab);

        var staffNames = await _db.Staffs.AsNoTracking()
            .Select(s => new { s.StaffId, s.FullName, s.Username })
            .ToDictionaryAsync(s => s.StaffId, s => string.IsNullOrWhiteSpace(s.FullName) ? s.Username : s.FullName, ct);

        var rows = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(500)
            .Select(x => new AdminOrderRow
            {
                OrderId = x.OrderId,
                ReceiverName = x.ReceiverName,
                ReceiverPhone = x.ReceiverPhone,
                ShippingAddress = x.ShippingAddress,
                PaymentMethodCode = x.PaymentMethodCode,
                StatusCode = x.StatusCode,
                TotalPriceVnd = x.TotalPriceVnd,
                DiscountAmountVnd = x.DiscountAmountVnd,
                CreatedAtUtc = x.CreatedAtUtc,
                AssignedStaffId = x.AssignedStaffId,
                PaymentCollectionStatus = x.PaymentCollectionStatus,
                DepositAmountVnd = x.DepositAmountVnd,
                DeliveredAtUtc = x.DeliveredAtUtc,
                DebtCollectedAtUtc = x.DebtCollectedAtUtc,
                OrderType = x.OrderType,
                TrackingNumber = x.TrackingNumber,
                ShippingProvider = x.ShippingProvider,
                ItemCount = x.Items.Count,
                ItemQty = x.Items.Sum(i => i.Qty)
            })
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            row.StatusCode = OrderStatusCatalog.Normalize(row.StatusCode);
            row.StatusDisplay = OrderStatusCatalog.ToDisplayName(row.StatusCode);
            row.StatusBadgeClass = OrderStatusCatalog.ToBadgeClass(row.StatusCode);
            row.CollectionDisplay = PaymentCollectionCatalog.ToDisplayName(row.PaymentCollectionStatus);
            row.IsDebt = OrderStatusCatalog.IsDebtOutstanding(row.StatusCode, row.PaymentCollectionStatus);
            row.IsLikelyBuildPc = row.ItemCount >= 4 || row.ItemQty >= 6;
            row.AllowedActions = OrderWorkflowActions.AllowedActionsForSales(row.StatusCode, row.PaymentCollectionStatus).ToList();
            if (row.AssignedStaffId is int sid && staffNames.TryGetValue(sid, out var name))
                row.AssignedStaffName = name;
        }

        var staffOptions = await _db.Staffs.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.FullName)
            .Select(x => new SelectListItem
            {
                Value = x.StaffId.ToString(),
                Text = string.IsNullOrWhiteSpace(x.FullName) ? x.Username : $"{x.FullName} ({x.Username})"
            })
            .ToListAsync(ct);

        return View(new AdminOrdersViewModel
        {
            Keyword = keyword,
            Tab = activeTab,
            FromDate = fromDate?.Date,
            ToDate = toDate?.Date,
            Counts = counts,
            Rows = rows,
            StaffOptions = staffOptions
        });
    }

    [HttpGet]
    public IActionResult Debts(string? keyword, DateTime? fromDate, DateTime? toDate)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;
        return RedirectToAction(nameof(Orders), new
        {
            tab = "debt",
            keyword,
            fromDate = fromDate?.ToString("yyyy-MM-dd"),
            toDate = toDate?.ToString("yyyy-MM-dd")
        });
    }

    /// <summary>UC11 — Quản lý đóng gói / tạo vận đơn (kho).</summary>
    [HttpGet]
    public async Task<IActionResult> Packing(string? keyword, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var q = _db.Orders.AsNoTracking()
            .Where(x =>
                x.StatusCode == OrderStatusCatalog.Approved
                || x.StatusCode == OrderStatusCatalog.Packed
                || x.StatusCode == OrderStatusCatalog.ReadyToDeliver
                || x.StatusCode == OrderStatusCatalog.IssueDoa
                || x.StatusCode == OrderStatusCatalog.IssueCaseMismatch);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            q = q.Where(x =>
                x.ReceiverName.Contains(k) ||
                x.ReceiverPhone.Contains(k) ||
                x.ShippingAddress.Contains(k) ||
                x.OrderId.ToString().Contains(k) ||
                (x.TrackingNumber != null && x.TrackingNumber.Contains(k)));
        }

        var staffOptions = await _db.Staffs.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.FullName)
            .Select(x => new SelectListItem
            {
                Value = x.StaffId.ToString(),
                Text = string.IsNullOrWhiteSpace(x.FullName) ? x.Username : $"{x.FullName} ({x.Username})"
            })
            .ToListAsync(ct);

        var rows = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(200)
            .Select(x => new AdminPackingRow
            {
                OrderId = x.OrderId,
                ReceiverName = x.ReceiverName,
                ReceiverPhone = x.ReceiverPhone,
                ShippingAddress = x.ShippingAddress,
                PaymentMethodCode = x.PaymentMethodCode,
                StatusCode = x.StatusCode,
                TotalPriceVnd = x.TotalPriceVnd,
                DiscountAmountVnd = x.DiscountAmountVnd,
                DepositAmountVnd = x.DepositAmountVnd,
                CreatedAtUtc = x.CreatedAtUtc,
                AssignedStaffId = x.AssignedStaffId,
                TrackingNumber = x.TrackingNumber,
                ShippingProvider = x.ShippingProvider
            })
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            row.StatusCode = OrderStatusCatalog.Normalize(row.StatusCode);
            row.StatusDisplay = OrderStatusCatalog.ToDisplayName(row.StatusCode);
            row.StatusBadgeClass = OrderStatusCatalog.ToBadgeClass(row.StatusCode);
            row.CodAmountVnd = CreateShipmentService.ComputeCodAmount(new OrderEntity
            {
                PaymentMethodCode = row.PaymentMethodCode,
                TotalPriceVnd = row.TotalPriceVnd,
                DepositAmountVnd = row.DepositAmountVnd
            });
            row.ProviderDisplay = ShippingProviderCatalog.ToDisplayName(row.ShippingProvider);
        }

        return View(new AdminPackingViewModel
        {
            Keyword = keyword,
            Rows = rows,
            StaffOptions = staffOptions,
            ProviderOptions =
            [
                new SelectListItem(ShippingProviderCatalog.ToDisplayName(ShippingProviderCatalog.Ghtk), ShippingProviderCatalog.Ghtk),
                new SelectListItem(ShippingProviderCatalog.ToDisplayName(ShippingProviderCatalog.ViettelPost), ShippingProviderCatalog.ViettelPost),
                new SelectListItem(ShippingProviderCatalog.ToDisplayName(ShippingProviderCatalog.ExpressStore), ShippingProviderCatalog.ExpressStore)
            ]
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkPacked(int orderId, string? keyword, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var result = await _shipments.MarkPackedAsync(orderId, ct);
        TempData[result.Ok ? "AdminInfo" : "AdminError"] = result.Message;
        return RedirectToAction(nameof(Packing), new { keyword });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkExported(int orderId, string? keyword, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var result = await _assembly.MarkExportedAsync(orderId, User.Identity?.Name, ct);
        TempData[result.Ok ? "AdminInfo" : "AdminError"] = result.Message;
        return RedirectToAction(nameof(Packing), new { keyword });
    }

    /// <summary>UC11 — Tạo vận đơn (mock API GHTK/Viettel hoặc giao hỏa tốc).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateShipment(
        int orderId,
        string providerCode,
        int? staffId,
        string? keyword,
        CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var result = await _shipments.CreateAsync(orderId, providerCode, staffId, ct);
        if (!result.Ok)
        {
            TempData["AdminError"] = result.Message;
            return RedirectToAction(nameof(Packing), new { keyword });
        }

        TempData["AdminInfo"] = result.Message;
        return RedirectToAction(nameof(PrintShippingLabel), new { orderId });
    }

    /// <summary>UC11 — Tem vận đơn (tự gọi window.print).</summary>
    [HttpGet]
    public async Task<IActionResult> PrintShippingLabel(int orderId, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var order = await _db.Orders.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return NotFound();

        if (string.IsNullOrWhiteSpace(order.TrackingNumber))
        {
            TempData["AdminError"] = "Đơn chưa có mã vận đơn. Hãy tạo vận đơn trước.";
            return RedirectToAction(nameof(Packing));
        }

        var vm = new ShippingLabelViewModel
        {
            OrderId = order.OrderId,
            TrackingNumber = order.TrackingNumber!,
            ShippingProvider = order.ShippingProvider ?? "",
            ProviderDisplay = ShippingProviderCatalog.ToDisplayName(order.ShippingProvider),
            ReceiverName = order.ReceiverName,
            ReceiverPhone = order.ReceiverPhone,
            ShippingAddress = order.ShippingAddress,
            PaymentMethodCode = order.PaymentMethodCode,
            CodAmountVnd = CreateShipmentService.ComputeCodAmount(order),
            TotalPriceVnd = order.TotalPriceVnd,
            Note = order.Note,
            CreatedAtUtc = order.CreatedAtUtc,
            ShippedAtUtc = order.ShippedAtUtc
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveOrder(int orderId, string? tab, string? keyword, DateTime? fromDate, DateTime? toDate, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var result = await _fulfillment.ApproveAsync(orderId, ct);
        TempData["AdminInfo"] = result.Message;
        return RedirectToOrders(tab, keyword, fromDate, toDate);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignShipper(
        int orderId,
        int staffId,
        string? tab,
        string? keyword,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var result = await _fulfillment.AssignAndShipAsync(orderId, staffId, ct);
        TempData["AdminInfo"] = result.Message;
        return RedirectToOrders(tab, keyword, fromDate, toDate);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmDelivery(
        int orderId,
        string outcome,
        decimal? depositAmountVnd,
        string? tab,
        string? keyword,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var result = await _fulfillment.ConfirmDeliveryAsync(orderId, outcome, depositAmountVnd, ct);
        TempData["AdminInfo"] = result.Message;
        return RedirectToOrders(tab, keyword, fromDate, toDate);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CollectDebt(
        int orderId,
        string? tab,
        string? keyword,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var result = await _fulfillment.CollectDebtAsync(orderId, ct);
        TempData["AdminInfo"] = result.Message;
        return RedirectToOrders(tab ?? "debt", keyword, fromDate, toDate);
    }

    /// <summary>
    /// Máy trạng thái đơn hàng — chỉ chấp nhận action hợp lệ theo Status hiện tại (chặn nhảy cóc).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyOrderAction(
        int orderId,
        string action,
        string? tab,
        string? keyword,
        DateTime? fromDate,
        DateTime? toDate,
        decimal? depositAmountVnd,
        CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var order = await _db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return NotFound();

        var act = (action ?? "").Trim().ToUpperInvariant();
        if (!OrderWorkflowActions.CanApply(order.StatusCode, act, order.PaymentCollectionStatus))
        {
            TempData["AdminError"] =
                $"Không thể thực hiện «{act}» khi đơn đang «{OrderStatusCatalog.ToDisplayName(order.StatusCode)}» (chặn nhảy cóc).";
            return RedirectToOrders(tab, keyword, fromDate, toDate);
        }

        var actor = User.Identity?.Name ?? "admin";
        string message;
        var ok = true;

        switch (act)
        {
            case OrderWorkflowActions.Approve:
            {
                var r = await _fulfillment.ApproveAsync(orderId, ct);
                ok = r.Ok;
                message = r.Message;
                break;
            }
            case OrderWorkflowActions.Cancel:
            {
                var r = await _cancellation.CancelAsync(orderId, phoneGuard: null, ct);
                ok = r.Succeeded;
                message = r.Succeeded
                    ? $"Đã hủy đơn #{orderId}."
                    : (r.Error ?? "Hủy đơn thất bại.");
                if (r.Succeeded && !string.IsNullOrWhiteSpace(r.NewStatus))
                    await _timeline.AppendAsync(orderId, r.NewStatus, "Admin hủy đơn.", actor, ct: ct);
                break;
            }
            case OrderWorkflowActions.Pack:
            {
                var r = await _shipments.MarkPackedAsync(orderId, ct);
                ok = r.Ok;
                message = r.Message;
                break;
            }
            case OrderWorkflowActions.ExportToTech:
            {
                var r = await _assembly.MarkExportedAsync(orderId, actor, ct);
                ok = r.Ok;
                message = r.Message;
                break;
            }
            case OrderWorkflowActions.DeliverSuccess:
            {
                var r = await _fulfillment.ConfirmDeliveryAsync(orderId, "COLLECTED", null, ct);
                ok = r.Ok;
                message = r.Message;
                break;
            }
            case OrderWorkflowActions.DeliverFailed:
            {
                var r = await _fulfillment.ConfirmDeliveryAsync(orderId, "FAILED", null, ct);
                ok = r.Ok;
                message = r.Message;
                break;
            }
            case OrderWorkflowActions.DeliverUnpaid:
            {
                var r = await _fulfillment.ConfirmDeliveryAsync(orderId, "DEPOSIT", depositAmountVnd, ct);
                ok = r.Ok;
                message = r.Message;
                break;
            }
            case OrderWorkflowActions.CollectDebt:
            {
                var r = await _fulfillment.CollectDebtAsync(orderId, ct);
                ok = r.Ok;
                message = r.Message;
                break;
            }
            default:
                ok = false;
                message = "Hành động không được hỗ trợ.";
                break;
        }

        TempData[ok ? "AdminInfo" : "AdminError"] = message;
        return RedirectToOrders(tab, keyword, fromDate, toDate);
    }

    [Obsolete("Dùng ApplyOrderAction — không cho đổi status tự do.")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateOrderStatus(
        int orderId,
        string statusCode,
        string? tab,
        string? keyword,
        DateTime? fromDate,
        DateTime? toDate)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        TempData["AdminError"] =
            "Đã tắt đổi trạng thái thủ công. Hãy dùng các nút thao tác theo đúng quy trình đơn hàng.";
        return RedirectToOrders(tab, keyword, fromDate, toDate);
    }

    private IActionResult RedirectToOrders(string? tab, string? keyword, DateTime? fromDate, DateTime? toDate)
    {
        return RedirectToAction(nameof(Orders), new
        {
            tab = NormalizeOrdersTab(tab),
            keyword,
            fromDate = fromDate?.ToString("yyyy-MM-dd"),
            toDate = toDate?.ToString("yyyy-MM-dd")
        });
    }

    private static string NormalizeOrdersTab(string? tab)
    {
        var t = (tab ?? "all").Trim().ToLowerInvariant();
        return t is "pending" or "approved" or "packed" or "shipping" or "cancelled" or "debt" or "failed"
            ? t
            : "all";
    }

    private static IQueryable<OrderEntity> ApplyOrdersTabFilter(IQueryable<OrderEntity> q, string tab)
    {
        return tab switch
        {
            "pending" => q.Where(x =>
                x.StatusCode == OrderStatusCatalog.PendingConfirmation
                || x.StatusCode == "PENDING"
                || x.StatusCode == "PROCESSING"
                || x.StatusCode == "WAITING_PARTS"
                || x.StatusCode == OrderStatusCatalog.WaitingPayment),
            "approved" => q.Where(x => x.StatusCode == OrderStatusCatalog.Approved),
            "packed" => q.Where(x => x.StatusCode == OrderStatusCatalog.Packed),
            "shipping" => q.Where(x =>
                x.StatusCode == OrderStatusCatalog.Shipping || x.StatusCode == "SHIPPED"),
            "cancelled" => q.Where(x =>
                x.StatusCode == OrderStatusCatalog.Cancelled
                || x.StatusCode == OrderStatusCatalog.CancelledNoStock
                || x.StatusCode == OrderStatusCatalog.Refunded),
            "debt" => q.Where(x =>
                x.StatusCode == OrderStatusCatalog.DeliveredUnpaid
                || ((x.StatusCode == OrderStatusCatalog.DeliveredCollected
                     || x.StatusCode == OrderStatusCatalog.PaidCompleted)
                    && x.PaymentCollectionStatus == PaymentCollectionCatalog.Uncollected)),
            "failed" => q.Where(x => x.StatusCode == OrderStatusCatalog.DeliveryFailed),
            _ => q
        };
    }

    private static DateTime? StartOfLocalDayUtc(DateTime? localDate)
    {
        if (!localDate.HasValue) return null;
        var tz = ResolveVietnamTimeZone();
        var local = DateTime.SpecifyKind(localDate.Value.Date, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    private static DateTime? EndOfLocalDayUtcExclusive(DateTime? localDate)
    {
        if (!localDate.HasValue) return null;
        var tz = ResolveVietnamTimeZone();
        var nextLocal = DateTime.SpecifyKind(localDate.Value.Date.AddDays(1), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(nextLocal, tz);
    }

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> WaitlistDelete(int stockWaitlistId, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var row = await _db.StockWaitlists.SingleOrDefaultAsync(x => x.StockWaitlistId == stockWaitlistId, ct);
        if (row is not null)
        {
            _db.StockWaitlists.Remove(row);
            await _db.SaveChangesAsync(ct);
            TempData["AdminInfo"] = "Đã xóa đăng ký theo dõi.";
        }
        return RedirectToAction(nameof(Waitlist));
    }

    private IActionResult? EnsureAdmin() =>
        AdminPortalAccess.EnsureCanAccess(this, User, nameof(Index), "Admin");

    private static void ValidateProductForm(AdminProductEditViewModel vm)
    {
        vm.Sku = vm.Sku?.Trim() ?? string.Empty;
        vm.Name = vm.Name?.Trim() ?? string.Empty;
    }

    private async Task FillProductEditOptionsAsync(AdminProductEditViewModel vm, CancellationToken ct)
    {
        vm.Categories = await _db.ComponentCategories.AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .Select(x => new SelectListItem(x.DisplayName, x.ComponentCategoryId.ToString()))
            .ToListAsync(ct);

        vm.Brands = await _db.Brands.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.BrandId.ToString()))
            .ToListAsync(ct);
    }
}

public sealed class AdminDashboardViewModel
{
    public int TotalProducts { get; set; }
    public int ActiveProducts { get; set; }
    public int OutOfStockProducts { get; set; }
    public int LowStockProducts { get; set; }
    public int WaitlistRows { get; set; }
    public int TotalOrders { get; set; }
    public List<AdminStatusCountRow> OrderStatusRows { get; set; } = [];
}

public sealed class AdminRevenueViewModel
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? StatusCode { get; set; }
    public string Payment { get; set; } = "all";
    public List<SelectListItem> StatusOptions { get; set; } = [];
    public RevenueReportResult Report { get; set; } = new();
}

public sealed record AdminStatusCountRow(string StatusName, int Count);

public sealed class AdminProductsViewModel
{
    public string? Keyword { get; set; }
    public string? Group { get; set; }
    public string? CategoryCode { get; set; }
    public string? Status { get; set; }
    public int TotalCount { get; set; }
    public int ComponentCount { get; set; }
    public int PrebuiltCount { get; set; }
    public int WorkstationCount { get; set; }
    public List<SelectListItem> CategoryOptions { get; set; } = [];
    public List<AdminCategoryCountRow> CategoryCounts { get; set; } = [];
    public List<AdminProductRow> Rows { get; set; } = [];
}

public sealed class AdminCategoryCountRow
{
    public string CategoryCode { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public int Count { get; set; }
}

public sealed class AdminProductRow
{
    public int ComponentId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string CategoryCode { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public decimal PriceVnd { get; set; }
    public int StockQty { get; set; }
    public bool IsActive { get; set; }
    public string ImageUrl { get; set; } = "";
}

public sealed class AdminProductEditViewModel
{
    public int ComponentId { get; set; }
    public int ComponentCategoryId { get; set; }
    public int? BrandId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal PriceVnd { get; set; }
    public int StockQty { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsHot { get; set; }
    public bool IsBestSeller { get; set; }
    public string? CurrentImageUrl { get; set; }
    public IFormFile? ImageUpload { get; set; }
    public List<SelectListItem> Categories { get; set; } = [];
    public List<SelectListItem> Brands { get; set; } = [];
}

public sealed class AdminInventoryViewModel
{
    public string? Keyword { get; set; }
    public string? StockFilter { get; set; }
    public List<AdminInventoryRow> Rows { get; set; } = [];
}

public sealed class AdminInventoryRow
{
    public int ComponentId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public int StockQty { get; set; }
    public bool IsActive { get; set; }
}

public sealed class AdminWaitlistRow
{
    public int StockWaitlistId { get; set; }
    public int ComponentId { get; set; }
    public string ComponentSku { get; set; } = "";
    public string ComponentName { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AdminOrdersViewModel
{
    public string? Keyword { get; set; }
    public string Tab { get; set; } = "all";
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public AdminOrdersTabCounts Counts { get; set; } = new();
    public List<SelectListItem> StatusOptions { get; set; } = [];
    public List<SelectListItem> StaffOptions { get; set; } = [];
    public List<AdminOrderRow> Rows { get; set; } = [];
}

public sealed class AdminOrdersTabCounts
{
    public int All { get; set; }
    public int Pending { get; set; }
    public int Approved { get; set; }
    public int Packed { get; set; }
    public int Shipping { get; set; }
    public int Cancelled { get; set; }
    public int Debt { get; set; }
    public int Failed { get; set; }
}

public sealed class AdminOrderRow
{
    public int OrderId { get; set; }
    public string ReceiverName { get; set; } = "";
    public string ReceiverPhone { get; set; } = "";
    public string ShippingAddress { get; set; } = "";
    public string PaymentMethodCode { get; set; } = "";
    public string StatusCode { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
    public string StatusBadgeClass { get; set; } = "text-bg-light";
    public decimal TotalPriceVnd { get; set; }
    public decimal DiscountAmountVnd { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int? AssignedStaffId { get; set; }
    public string? AssignedStaffName { get; set; }
    public string PaymentCollectionStatus { get; set; } = "NONE";
    public string CollectionDisplay { get; set; } = "";
    public decimal? DepositAmountVnd { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? DebtCollectedAtUtc { get; set; }
    public string OrderType { get; set; } = "ONLINE";
    public bool IsDebt { get; set; }
    public string? TrackingNumber { get; set; }
    public string? ShippingProvider { get; set; }
    public int ItemCount { get; set; }
    public int ItemQty { get; set; }
    /// <summary>Gợi ý đơn Build PC (nhiều linh kiện) → ưu tiên xuất kho kỹ thuật.</summary>
    public bool IsLikelyBuildPc { get; set; }
    public List<string> AllowedActions { get; set; } = [];
}

public sealed class AdminPackingViewModel
{
    public string? Keyword { get; set; }
    public List<AdminPackingRow> Rows { get; set; } = [];
    public List<SelectListItem> StaffOptions { get; set; } = [];
    public List<SelectListItem> ProviderOptions { get; set; } = [];
}

public sealed class AdminPackingRow
{
    public int OrderId { get; set; }
    public string ReceiverName { get; set; } = "";
    public string ReceiverPhone { get; set; } = "";
    public string ShippingAddress { get; set; } = "";
    public string PaymentMethodCode { get; set; } = "";
    public string StatusCode { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
    public string StatusBadgeClass { get; set; } = "text-bg-light";
    public decimal TotalPriceVnd { get; set; }
    public decimal DiscountAmountVnd { get; set; }
    public decimal? DepositAmountVnd { get; set; }
    public decimal CodAmountVnd { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int? AssignedStaffId { get; set; }
    public string? TrackingNumber { get; set; }
    public string? ShippingProvider { get; set; }
    public string ProviderDisplay { get; set; } = "";
}

public sealed class ShippingLabelViewModel
{
    public int OrderId { get; set; }
    public string TrackingNumber { get; set; } = "";
    public string ShippingProvider { get; set; } = "";
    public string ProviderDisplay { get; set; } = "";
    public string ReceiverName { get; set; } = "";
    public string ReceiverPhone { get; set; } = "";
    public string ShippingAddress { get; set; } = "";
    public string PaymentMethodCode { get; set; } = "";
    public decimal CodAmountVnd { get; set; }
    public decimal TotalPriceVnd { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ShippedAtUtc { get; set; }
}

