namespace ban_link_kien_PC.Domain.Orders;

/// <summary>Ghi lịch sử thay đổi trạng thái đơn (hiển thị tiến độ cho khách / kỹ thuật).</summary>
public sealed class OrderTimelineService
{
    private readonly Infrastructure.Persistence.PcStoreDbContext _db;

    public OrderTimelineService(Infrastructure.Persistence.PcStoreDbContext db) => _db = db;

    public async Task AppendAsync(
        int orderId,
        string statusCode,
        string? note = null,
        string? changedBy = null,
        string? issueReasonCode = null,
        CancellationToken ct = default)
    {
        _db.OrderTimelines.Add(new Infrastructure.Persistence.Entities.OrderTimelineEntity
        {
            OrderId = orderId,
            StatusCode = OrderStatusCatalog.Normalize(statusCode),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ChangedBy = string.IsNullOrWhiteSpace(changedBy) ? null : changedBy.Trim(),
            IssueReasonCode = string.IsNullOrWhiteSpace(issueReasonCode) ? null : issueReasonCode.Trim().ToUpperInvariant(),
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Ghi timeline trong cùng transaction với caller (caller SaveChanges sau).</summary>
    public void AppendPending(
        int orderId,
        string statusCode,
        string? note = null,
        string? changedBy = null,
        string? issueReasonCode = null)
    {
        _db.OrderTimelines.Add(new Infrastructure.Persistence.Entities.OrderTimelineEntity
        {
            OrderId = orderId,
            StatusCode = OrderStatusCatalog.Normalize(statusCode),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ChangedBy = string.IsNullOrWhiteSpace(changedBy) ? null : changedBy.Trim(),
            IssueReasonCode = string.IsNullOrWhiteSpace(issueReasonCode) ? null : issueReasonCode.Trim().ToUpperInvariant(),
            CreatedAtUtc = DateTime.UtcNow
        });
    }
}
