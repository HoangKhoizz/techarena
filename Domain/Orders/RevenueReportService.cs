using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Orders;

/// <summary>Báo cáo doanh thu Admin (Phase 6 — rubric 1.6).</summary>
public sealed class RevenueReportService
{
    private readonly PcStoreDbContext _db;

    public RevenueReportService(PcStoreDbContext db) => _db = db;

    /// <summary>Đơn đã thu / hoàn tất — tính vào doanh thu thực nhận.</summary>
    public static bool IsRecognizedRevenue(string? statusCode)
    {
        var n = OrderStatusCatalog.Normalize(statusCode);
        return n is OrderStatusCatalog.Paid
            or OrderStatusCatalog.PaidCompleted
            or OrderStatusCatalog.DeliveredCollected;
    }

    public static bool IsCod(string? paymentMethodCode)
    {
        var p = (paymentMethodCode ?? "").Trim().ToUpperInvariant();
        return p is "COD" or "SHIPCOD" or "SHIP_COD" or "CASH";
    }

    public static bool IsOnlinePayment(string? paymentMethodCode)
    {
        var p = (paymentMethodCode ?? "").Trim().ToUpperInvariant();
        return p is "MOMO" or "VNPAY" or "PAYPAL" or "BANK" or "ONLINE";
    }

    public async Task<RevenueReportResult> BuildAsync(
        DateTime? fromDate,
        DateTime? toDate,
        string? statusCode,
        string? paymentFilter,
        CancellationToken ct = default)
    {
        var fromUtc = StartOfLocalDayUtc(fromDate);
        var toUtcExclusive = EndOfLocalDayUtcExclusive(toDate);
        var statusFilter = string.IsNullOrWhiteSpace(statusCode)
            ? null
            : OrderStatusCatalog.Normalize(statusCode);
        var payFilter = (paymentFilter ?? "all").Trim().ToLowerInvariant();

        var q = _db.Orders.AsNoTracking().AsQueryable();
        if (fromUtc.HasValue)
            q = q.Where(x => x.CreatedAtUtc >= fromUtc.Value);
        if (toUtcExclusive.HasValue)
            q = q.Where(x => x.CreatedAtUtc < toUtcExclusive.Value);

        // Order trước Select — EF không dịch OrderBy trên property của record ctor
        var raw = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(5000)
            .Select(x => new RevenueOrderRaw(
                x.OrderId,
                x.ReceiverName,
                x.ReceiverPhone,
                x.PaymentMethodCode,
                x.StatusCode,
                x.TotalPriceVnd,
                x.DiscountAmountVnd,
                x.CreatedAtUtc,
                x.OrderType))
            .ToListAsync(ct);

        var normalized = raw.Select(x => new RevenueOrderRow
        {
            OrderId = x.OrderId,
            ReceiverName = x.ReceiverName,
            ReceiverPhone = x.ReceiverPhone,
            PaymentMethodCode = x.PaymentMethodCode,
            StatusCode = OrderStatusCatalog.Normalize(x.StatusCode),
            StatusDisplay = OrderStatusCatalog.ToDisplayName(x.StatusCode),
            TotalPriceVnd = x.TotalPriceVnd,
            DiscountAmountVnd = x.DiscountAmountVnd,
            CreatedAtUtc = x.CreatedAtUtc,
            OrderType = x.OrderType,
            IsRevenue = IsRecognizedRevenue(x.StatusCode),
            IsCod = IsCod(x.PaymentMethodCode),
            IsOnline = IsOnlinePayment(x.PaymentMethodCode)
        }).ToList();

        IEnumerable<RevenueOrderRow> filtered = normalized;
        if (statusFilter is not null)
            filtered = filtered.Where(x => x.StatusCode == statusFilter);

        filtered = payFilter switch
        {
            "cod" => filtered.Where(x => x.IsCod),
            "online" => filtered.Where(x => x.IsOnline),
            _ => filtered
        };

        var rows = filtered.Take(500).ToList();

        var byStatus = normalized
            .GroupBy(x => x.StatusCode)
            .Select(g => new RevenueStatusBucket
            {
                StatusCode = g.Key,
                StatusDisplay = OrderStatusCatalog.ToDisplayName(g.Key),
                OrderCount = g.Count(),
                TotalAmountVnd = g.Sum(x => x.TotalPriceVnd),
                RevenueAmountVnd = g.Where(x => x.IsRevenue).Sum(x => x.TotalPriceVnd)
            })
            .OrderByDescending(x => x.TotalAmountVnd)
            .ToList();

        // Áp dụng payment filter cho bảng trạng thái khi đang xem COD/Online
        if (payFilter is "cod" or "online")
        {
            var subset = payFilter == "cod"
                ? normalized.Where(x => x.IsCod)
                : normalized.Where(x => x.IsOnline);
            byStatus = subset
                .GroupBy(x => x.StatusCode)
                .Select(g => new RevenueStatusBucket
                {
                    StatusCode = g.Key,
                    StatusDisplay = OrderStatusCatalog.ToDisplayName(g.Key),
                    OrderCount = g.Count(),
                    TotalAmountVnd = g.Sum(x => x.TotalPriceVnd),
                    RevenueAmountVnd = g.Where(x => x.IsRevenue).Sum(x => x.TotalPriceVnd)
                })
                .OrderByDescending(x => x.TotalAmountVnd)
                .ToList();
        }

        var codRows = normalized.Where(x => x.IsCod).ToList();
        var onlineRows = normalized.Where(x => x.IsOnline).ToList();
        var scope = payFilter switch
        {
            "cod" => codRows,
            "online" => onlineRows,
            _ => normalized
        };
        if (statusFilter is not null)
            scope = scope.Where(x => x.StatusCode == statusFilter).ToList();

        return new RevenueReportResult
        {
            FromDate = fromDate?.Date,
            ToDate = toDate?.Date,
            StatusFilter = statusFilter,
            PaymentFilter = payFilter is "cod" or "online" ? payFilter : "all",
            TotalOrders = scope.Count,
            TotalAmountVnd = scope.Sum(x => x.TotalPriceVnd),
            RecognizedRevenueVnd = scope.Where(x => x.IsRevenue).Sum(x => x.TotalPriceVnd),
            RecognizedOrderCount = scope.Count(x => x.IsRevenue),
            CodOrderCount = codRows.Count,
            CodAmountVnd = codRows.Sum(x => x.TotalPriceVnd),
            CodRevenueVnd = codRows.Where(x => x.IsRevenue).Sum(x => x.TotalPriceVnd),
            OnlineOrderCount = onlineRows.Count,
            OnlineAmountVnd = onlineRows.Sum(x => x.TotalPriceVnd),
            OnlineRevenueVnd = onlineRows.Where(x => x.IsRevenue).Sum(x => x.TotalPriceVnd),
            ByStatus = byStatus,
            Orders = rows
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

    private sealed record RevenueOrderRaw(
        int OrderId,
        string ReceiverName,
        string ReceiverPhone,
        string PaymentMethodCode,
        string StatusCode,
        decimal TotalPriceVnd,
        decimal DiscountAmountVnd,
        DateTime CreatedAtUtc,
        string OrderType);
}

public sealed class RevenueReportResult
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? StatusFilter { get; set; }
    public string PaymentFilter { get; set; } = "all";
    public int TotalOrders { get; set; }
    public decimal TotalAmountVnd { get; set; }
    public decimal RecognizedRevenueVnd { get; set; }
    public int RecognizedOrderCount { get; set; }
    public int CodOrderCount { get; set; }
    public decimal CodAmountVnd { get; set; }
    public decimal CodRevenueVnd { get; set; }
    public int OnlineOrderCount { get; set; }
    public decimal OnlineAmountVnd { get; set; }
    public decimal OnlineRevenueVnd { get; set; }
    public List<RevenueStatusBucket> ByStatus { get; set; } = [];
    public List<RevenueOrderRow> Orders { get; set; } = [];
}

public sealed class RevenueStatusBucket
{
    public string StatusCode { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
    public int OrderCount { get; set; }
    public decimal TotalAmountVnd { get; set; }
    public decimal RevenueAmountVnd { get; set; }
}

public sealed class RevenueOrderRow
{
    public int OrderId { get; set; }
    public string ReceiverName { get; set; } = "";
    public string ReceiverPhone { get; set; } = "";
    public string PaymentMethodCode { get; set; } = "";
    public string StatusCode { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
    public decimal TotalPriceVnd { get; set; }
    public decimal DiscountAmountVnd { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string OrderType { get; set; } = "";
    public bool IsRevenue { get; set; }
    public bool IsCod { get; set; }
    public bool IsOnline { get; set; }
}
