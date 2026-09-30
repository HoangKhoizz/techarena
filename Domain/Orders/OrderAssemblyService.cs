using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Orders;

/// <summary>Quy trình lắp ráp PC: Xuất kho → Ráp → Sẵn sàng giao / Báo lỗi DOA-Case.</summary>
public sealed class OrderAssemblyService
{
    public const string IssueDoa = "DOA";
    public const string IssueCaseMismatch = "CASE_MISMATCH";

    private readonly PcStoreDbContext _db;
    private readonly OrderTimelineService _timeline;

    public OrderAssemblyService(PcStoreDbContext db, OrderTimelineService timeline)
    {
        _db = db;
        _timeline = timeline;
    }

    /// <summary>Kho xuất linh kiện cho kỹ thuật: APPROVED/PACKED → EXPORTED.</summary>
    public async Task<(bool Ok, string Message)> MarkExportedAsync(
        int orderId,
        string? changedBy = null,
        CancellationToken ct = default)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return (false, "Không tìm thấy đơn hàng.");

        var status = OrderStatusCatalog.Normalize(order.StatusCode);
        if (status is not OrderStatusCatalog.Approved and not OrderStatusCatalog.Packed
            and not OrderStatusCatalog.IssueDoa and not OrderStatusCatalog.IssueCaseMismatch)
            return (false, "Chỉ xuất kho đơn Đã duyệt / Đã đóng gói / đang tạm ngưng lỗi (sau khi đổi linh kiện).");

        order.StatusCode = OrderStatusCatalog.Exported;
        _timeline.AppendPending(orderId, OrderStatusCatalog.Exported,
            note: "Đã xuất kho linh kiện cho kỹ thuật lắp ráp.",
            changedBy: changedBy);
        await _db.SaveChangesAsync(ct);
        return (true, $"Đã xuất kho đơn #{orderId}.");
    }

    /// <summary>Kỹ thuật bắt đầu ráp: EXPORTED → ASSEMBLING.</summary>
    public async Task<(bool Ok, string Message)> StartAssemblingAsync(
        int orderId,
        string? changedBy = null,
        CancellationToken ct = default)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return (false, "Không tìm thấy đơn hàng.");

        var status = OrderStatusCatalog.Normalize(order.StatusCode);
        if (status != OrderStatusCatalog.Exported)
            return (false, "Chỉ bắt đầu ráp đơn đang ở trạng thái Đã xuất kho.");

        order.StatusCode = OrderStatusCatalog.Assembling;
        _timeline.AppendPending(orderId, OrderStatusCatalog.Assembling,
            note: "Kỹ thuật bắt đầu lắp ráp & test hiệu năng.",
            changedBy: changedBy);
        await _db.SaveChangesAsync(ct);
        return (true, $"Đã bắt đầu lắp ráp đơn #{orderId}.");
    }

    /// <summary>Hoàn tất lắp ráp: ASSEMBLING → READY_TO_DELIVER.</summary>
    public async Task<(bool Ok, string Message)> CompleteAssemblyAsync(
        int orderId,
        string? changedBy = null,
        CancellationToken ct = default)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return (false, "Không tìm thấy đơn hàng.");

        var status = OrderStatusCatalog.Normalize(order.StatusCode);
        if (status != OrderStatusCatalog.Assembling)
            return (false, "Chỉ hoàn tất lắp ráp khi đơn đang Đang lắp ráp.");

        order.StatusCode = OrderStatusCatalog.ReadyToDeliver;
        _timeline.AppendPending(orderId, OrderStatusCatalog.ReadyToDeliver,
            note: "Hoàn tất lắp ráp — sẵn sàng giao hàng.",
            changedBy: changedBy);
        await _db.SaveChangesAsync(ct);
        return (true, $"Đơn #{orderId} đã sẵn sàng giao.");
    }

    /// <summary>Báo lỗi tạm ngưng: ASSEMBLING|EXPORTED → ISSUE_DOA / ISSUE_CASE_MISMATCH.</summary>
    public async Task<(bool Ok, string Message)> ReportIssueAsync(
        int orderId,
        string issueReasonCode,
        string? note = null,
        string? changedBy = null,
        CancellationToken ct = default)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (order is null) return (false, "Không tìm thấy đơn hàng.");

        var status = OrderStatusCatalog.Normalize(order.StatusCode);
        if (status is not OrderStatusCatalog.Assembling and not OrderStatusCatalog.Exported)
            return (false, "Chỉ báo lỗi khi đơn Đã xuất kho hoặc Đang lắp ráp.");

        var reason = (issueReasonCode ?? "").Trim().ToUpperInvariant();
        string newStatus;
        string displayReason;
        switch (reason)
        {
            case IssueDoa:
            case "ISSUE_DOA":
                newStatus = OrderStatusCatalog.IssueDoa;
                displayReason = "Linh kiện bị lỗi (DOA)";
                reason = IssueDoa;
                break;
            case IssueCaseMismatch:
            case "ISSUE_CASE_MISMATCH":
            case "CASE":
                newStatus = OrderStatusCatalog.IssueCaseMismatch;
                displayReason = "Không vừa vỏ Case";
                reason = IssueCaseMismatch;
                break;
            default:
                return (false, "Nguyên nhân lỗi không hợp lệ (DOA / CASE_MISMATCH).");
        }

        var fullNote = string.IsNullOrWhiteSpace(note)
            ? $"Tạm ngưng lắp ráp: {displayReason}."
            : $"Tạm ngưng lắp ráp: {displayReason}. {note.Trim()}";

        order.StatusCode = newStatus;
        _timeline.AppendPending(orderId, newStatus,
            note: fullNote,
            changedBy: changedBy,
            issueReasonCode: reason);
        await _db.SaveChangesAsync(ct);
        return (true, $"Đã báo lỗi đơn #{orderId}: {displayReason}. Sales/Kho sẽ xử lý đổi linh kiện.");
    }

    public static string IssueReasonDisplay(string? code) =>
        (code ?? "").Trim().ToUpperInvariant() switch
        {
            IssueDoa or "ISSUE_DOA" => "Linh kiện bị lỗi (DOA)",
            IssueCaseMismatch or "ISSUE_CASE_MISMATCH" or "CASE" => "Không vừa vỏ Case",
            _ => OrderStatusCatalog.ToDisplayName(code)
        };
}
