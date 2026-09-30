using ban_link_kien_PC.Domain.Customers;
using ban_link_kien_PC.Infrastructure.Persistence;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Orders;

public sealed record PosInvoiceItemRequest(int ComponentId, int Qty);

public sealed record CreateOfflineInvoiceRequest(
    string? ReceiverName,
    string? ReceiverPhone,
    string? ReceiverEmail,
    string? Note,
    string? PaymentMethodCode,
    decimal? AmountPaid,
    IReadOnlyList<PosInvoiceItemRequest> Items);

public sealed class CreateOfflineInvoiceResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public int? OrderId { get; init; }
    public decimal TotalPriceVnd { get; init; }
    public decimal? ChangeDue { get; init; }
    public string ReceiverName { get; init; } = "";
    public string ReceiverPhone { get; init; } = "";
    public DateTime? CreatedAtUtc { get; init; }
    public int? CustomerId { get; init; }

    public static CreateOfflineInvoiceResult Ok(
        int orderId,
        decimal total,
        decimal? change,
        string receiverName,
        string receiverPhone,
        DateTime createdAtUtc,
        int? customerId) => new()
    {
        Succeeded = true,
        OrderId = orderId,
        TotalPriceVnd = total,
        ChangeDue = change,
        ReceiverName = receiverName,
        ReceiverPhone = receiverPhone,
        CreatedAtUtc = createdAtUtc,
        CustomerId = customerId
    };

    public static CreateOfflineInvoiceResult Fail(string error) => new() { Succeeded = false, Error = error };
}

/// <summary>
/// Tạo hóa đơn POS (Order + OrderItem). Tồn kho do trigger TR_OrderItem_DeductStock_POS trừ.
/// </summary>
public sealed class PosInvoiceService
{
    private readonly PcStoreDbContext _db;
    private readonly LoyaltyService _loyalty;

    public PosInvoiceService(PcStoreDbContext db, LoyaltyService loyalty)
    {
        _db = db;
        _loyalty = loyalty;
    }

    public async Task<CreateOfflineInvoiceResult> CreateOfflineAsync(
        int createdByStaffId,
        CreateOfflineInvoiceRequest req,
        CancellationToken ct = default)
    {
        if (req.Items is null || req.Items.Count == 0)
            return CreateOfflineInvoiceResult.Fail("Hóa đơn phải có ít nhất 1 sản phẩm.");

        if (req.Items.Any(x => x.ComponentId <= 0 || x.Qty <= 0))
            return CreateOfflineInvoiceResult.Fail("ComponentId và Qty phải hợp lệ (> 0).");

        var staffExists = await _db.Staffs.AnyAsync(x => x.StaffId == createdByStaffId && x.IsActive, ct);
        if (!staffExists)
            return CreateOfflineInvoiceResult.Fail("Nhân viên lập hóa đơn không hợp lệ.");

        var demand = req.Items
            .GroupBy(x => x.ComponentId)
            .Select(g => new { ComponentId = g.Key, Qty = g.Sum(x => x.Qty) })
            .ToList();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var ids = demand.Select(x => x.ComponentId).ToList();
            var components = await _db.Components
                .Where(c => ids.Contains(c.ComponentId))
                .ToListAsync(ct);

            if (components.Count != ids.Count)
                return await FailAndRollback(tx, "Có sản phẩm không tồn tại trong hệ thống.", ct);

            foreach (var d in demand)
            {
                var c = components.Single(x => x.ComponentId == d.ComponentId);
                if (!c.IsActive)
                    return await FailAndRollback(tx, $"Sản phẩm {c.Sku} đã ngừng bán.", ct);
                if (c.StockQty < d.Qty)
                    return await FailAndRollback(tx,
                        $"Không đủ tồn kho cho {c.Name} (SKU {c.Sku}). Tồn={c.StockQty}, yêu cầu={d.Qty}.", ct);
            }

            decimal total = 0;
            var lineItems = new List<OrderItemEntity>();
            foreach (var item in req.Items)
            {
                var c = components.Single(x => x.ComponentId == item.ComponentId);
                total += c.PriceVnd * item.Qty;
                lineItems.Add(new OrderItemEntity
                {
                    ComponentId = item.ComponentId,
                    UnitPriceVnd = c.PriceVnd,
                    Qty = item.Qty
                });
            }

            var phone = string.IsNullOrWhiteSpace(req.ReceiverPhone) ? null : NormalizePhone(req.ReceiverPhone);
            var name = string.IsNullOrWhiteSpace(req.ReceiverName) ? "Khách tại quầy" : req.ReceiverName.Trim();

            int? customerId = null;
            if (!string.IsNullOrWhiteSpace(phone))
            {
                var customer = await _db.Customers
                    .FirstOrDefaultAsync(x => x.IsActive && x.Phone == phone, ct);
                if (customer is not null)
                {
                    customerId = customer.CustomerId;
                    if (string.IsNullOrWhiteSpace(req.ReceiverName) && !string.IsNullOrWhiteSpace(customer.FullName))
                        name = customer.FullName!;
                    if (string.IsNullOrWhiteSpace(customer.Phone))
                        customer.Phone = phone;
                }
            }

            var createdAt = DateTime.UtcNow;
            var order = new OrderEntity
            {
                CustomerId = customerId,
                CartId = null,
                ReceiverName = name,
                ReceiverPhone = phone ?? "0000000000",
                ReceiverEmail = string.IsNullOrWhiteSpace(req.ReceiverEmail) ? null : req.ReceiverEmail.Trim(),
                ShippingAddress = "Tại quầy",
                Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
                PaymentMethodCode = string.IsNullOrWhiteSpace(req.PaymentMethodCode) ? "CASH" : req.PaymentMethodCode.Trim().ToUpperInvariant(),
                TotalPriceVnd = total,
                StatusCode = "COMPLETED",
                OrderType = "POS",
                CreatedByStaffId = createdByStaffId,
                CreatedAtUtc = createdAt,
                Items = lineItems
            };

            _db.Orders.Add(order);

            // Không trừ StockQty ở đây — trigger TR_OrderItem_DeductStock_POS xử lý khi INSERT OrderItem.
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            if (customerId is not null)
                await _loyalty.TryAwardForOrderAsync(order.OrderId, ct);

            decimal? change = null;
            if (req.AmountPaid.HasValue)
                change = req.AmountPaid.Value - total;

            return CreateOfflineInvoiceResult.Ok(
                order.OrderId, total, change, order.ReceiverName, order.ReceiverPhone, createdAt, customerId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return CreateOfflineInvoiceResult.Fail($"Lỗi tạo hóa đơn: {ex.Message}");
        }
    }

    private static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits;
    }

    private static async Task<CreateOfflineInvoiceResult> FailAndRollback(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx,
        string error,
        CancellationToken ct)
    {
        await tx.RollbackAsync(ct);
        return CreateOfflineInvoiceResult.Fail(error);
    }
}
