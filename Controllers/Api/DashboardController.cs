using System.Globalization;
using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Infrastructure.Persistence;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Controllers.Api;

/// <summary>API báo cáo doanh thu cho Admin Dashboard.</summary>
[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController : ControllerBase
{
    private readonly PcStoreDbContext _db;

    public DashboardController(PcStoreDbContext db) => _db = db;

    [HttpGet("revenue-summary")]
    public async Task<IActionResult> RevenueSummary(CancellationToken ct)
    {
        if (!IsAdmin()) return Forbid();

        var (todayStartUtc, weekStartUtc, monthStartUtc, nowLocal) = GetPeriodBounds();

        var todayRows = await _db.Orders.AsNoTracking()
            .Where(o => o.CreatedAtUtc >= todayStartUtc)
            .Select(o => new { o.StatusCode, o.TotalPriceVnd })
            .ToListAsync(ct);
        var weekRows = await _db.Orders.AsNoTracking()
            .Where(o => o.CreatedAtUtc >= weekStartUtc)
            .Select(o => new { o.StatusCode, o.TotalPriceVnd })
            .ToListAsync(ct);
        var monthRows = await _db.Orders.AsNoTracking()
            .Where(o => o.CreatedAtUtc >= monthStartUtc)
            .Select(o => new { o.StatusCode, o.TotalPriceVnd })
            .ToListAsync(ct);

        var todayPaid = todayRows.Where(o => RevenueReportService.IsRecognizedRevenue(o.StatusCode)).ToList();
        var weekPaid = weekRows.Where(o => RevenueReportService.IsRecognizedRevenue(o.StatusCode)).ToList();
        var monthPaid = monthRows.Where(o => RevenueReportService.IsRecognizedRevenue(o.StatusCode)).ToList();

        return Ok(new
        {
            todayRevenue = todayPaid.Sum(x => x.TotalPriceVnd),
            weekRevenue = weekPaid.Sum(x => x.TotalPriceVnd),
            monthRevenue = monthPaid.Sum(x => x.TotalPriceVnd),
            todayOrderCount = todayPaid.Count,
            weekOrderCount = weekPaid.Count,
            monthOrderCount = monthPaid.Count,
            asOfLocal = nowLocal.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
        });
    }

    /// <summary>Mảng doanh thu 30 ngày gần nhất (để vẽ Line Chart).</summary>
    [HttpGet("revenue-last-30-days")]
    public async Task<IActionResult> RevenueLast30Days(CancellationToken ct)
    {
        if (!IsAdmin()) return Forbid();

        var tz = GetVietnamTimeZone();
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var startLocalDate = nowLocal.Date.AddDays(-29);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocalDate, tz);

        var rows = await _db.Orders.AsNoTracking()
            .Where(o => o.CreatedAtUtc >= startUtc)
            .Select(o => new { o.CreatedAtUtc, o.TotalPriceVnd, o.StatusCode })
            .ToListAsync(ct);

        var paid = rows.Where(o => RevenueReportService.IsRecognizedRevenue(o.StatusCode));

        var byDay = paid
            .GroupBy(o => TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(o.CreatedAtUtc, DateTimeKind.Utc), tz).Date)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalPriceVnd));

        var series = new List<object>(30);
        for (var i = 0; i < 30; i++)
        {
            var day = startLocalDate.AddDays(i);
            byDay.TryGetValue(day, out var revenue);
            series.Add(new
            {
                date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                label = day.ToString("dd/MM", CultureInfo.InvariantCulture),
                revenue
            });
        }

        return Ok(new { days = series });
    }

    /// <summary>Xuất Excel đơn thành công trong tháng hiện tại.</summary>
    [HttpGet("export-excel")]
    public async Task<IActionResult> ExportExcel(CancellationToken ct)
    {
        if (!IsAdmin()) return Forbid();

        var (_, _, monthStartUtc, nowLocal) = GetPeriodBounds();
        var monthEndUtc = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(nowLocal.Year, nowLocal.Month, 1).AddMonths(1), GetVietnamTimeZone());

        var all = await _db.Orders.AsNoTracking()
            .Where(o => o.CreatedAtUtc >= monthStartUtc && o.CreatedAtUtc < monthEndUtc)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new
            {
                o.OrderId,
                o.CreatedAtUtc,
                o.ReceiverName,
                o.TotalPriceVnd,
                o.StatusCode,
                o.PaymentMethodCode
            })
            .ToListAsync(ct);

        var orders = all.Where(o => RevenueReportService.IsRecognizedRevenue(o.StatusCode)).ToList();

        var tz = GetVietnamTimeZone();
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Doanh thu tháng");

        ws.Cell(1, 1).Value = "Mã Đơn";
        ws.Cell(1, 2).Value = "Ngày";
        ws.Cell(1, 3).Value = "Tên Khách";
        ws.Cell(1, 4).Value = "Tổng Tiền";
        ws.Cell(1, 5).Value = "Trạng thái";
        ws.Cell(1, 6).Value = "Thanh toán";

        var header = ws.Range(1, 1, 1, 6);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F766E");
        header.Style.Font.FontColor = XLColor.White;

        var row = 2;
        foreach (var o in orders)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(o.CreatedAtUtc, DateTimeKind.Utc), tz);
            ws.Cell(row, 1).Value = o.OrderId;
            ws.Cell(row, 2).Value = local;
            ws.Cell(row, 2).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            ws.Cell(row, 3).Value = o.ReceiverName;
            ws.Cell(row, 4).Value = o.TotalPriceVnd;
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 5).Value = OrderStatusCatalog.ToDisplayName(o.StatusCode);
            ws.Cell(row, 6).Value = o.PaymentMethodCode;
            row++;
        }

        if (orders.Count > 0)
        {
            ws.Cell(row, 3).Value = "Tổng cộng";
            ws.Cell(row, 3).Style.Font.Bold = true;
            ws.Cell(row, 4).FormulaA1 = $"SUM(D2:D{row - 1})";
            ws.Cell(row, 4).Style.Font.Bold = true;
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
        }

        ws.Columns().AdjustToContents();

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var fileName = $"DoanhThu_{nowLocal:yyyyMM}.xlsx";
        return File(
            stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private bool IsAdmin() =>
        User.Identity?.IsAuthenticated == true &&
        string.Equals(User.Identity.Name, "admin", StringComparison.OrdinalIgnoreCase);

    private static TimeZoneInfo GetVietnamTimeZone()
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

    private static (DateTime todayStartUtc, DateTime weekStartUtc, DateTime monthStartUtc, DateTime nowLocal)
        GetPeriodBounds()
    {
        var tz = GetVietnamTimeZone();
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var todayStartLocal = nowLocal.Date;
        var weekStartLocal = todayStartLocal.AddDays(-6);
        var monthStartLocal = new DateTime(nowLocal.Year, nowLocal.Month, 1);

        return (
            TimeZoneInfo.ConvertTimeToUtc(todayStartLocal, tz),
            TimeZoneInfo.ConvertTimeToUtc(weekStartLocal, tz),
            TimeZoneInfo.ConvertTimeToUtc(monthStartLocal, tz),
            nowLocal
        );
    }
}
