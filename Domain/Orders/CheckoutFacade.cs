using ban_link_kien_PC.Domain.Promotions;
using ban_link_kien_PC.Infrastructure.Persistence;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Domain.Orders;

public sealed record CheckoutRequest(
    string ReceiverName,
    string ReceiverPhone,
    string? ReceiverEmail,
    string ShippingAddress,
    string? Note,
    string PaymentMethodCode = "COD",
    string? PromoCode = null);

public sealed record CheckoutResult(bool Succeeded, int? OrderId, string? Error, string? PaymentMethodCode = null)
{
    public static CheckoutResult Ok(int orderId, string paymentMethod) => new(true, orderId, null, paymentMethod);
    public static CheckoutResult Fail(string error) => new(false, null, error, null);
}

/// <summary>
/// Guest checkout: CustomerId có thể null; bắt buộc ReceiverName, ReceiverPhone, ShippingAddress.
/// Trừ kho ngay khi tạo đơn ONLINE (COD + MoMo). Áp mã KM + ưu đãi hạng.
/// </summary>
public sealed class CheckoutFacade
{
    private readonly PcStoreDbContext _db;
    private readonly CartManager _cart;
    private readonly InventoryStockService _stock;
    private readonly PromoService _promo;

    public CheckoutFacade(
        PcStoreDbContext db,
        CartManager cart,
        InventoryStockService stock,
        PromoService promo)
    {
        _db = db;
        _cart = cart;
        _stock = stock;
        _promo = promo;
    }

    public Task<CheckoutResult> PlaceCodOrderAsync(
        string cartKey,
        int? customerId,
        CheckoutRequest req,
        CancellationToken ct = default)
        => PlaceOrderAsync(cartKey, customerId, req with { PaymentMethodCode = "COD" }, ct);

    public async Task<CheckoutResult> PlaceOrderAsync(
        string cartKey,
        int? customerId,
        CheckoutRequest req,
        CancellationToken ct = default)
    {
        var receiverName = req.ReceiverName.Trim();
        var receiverPhone = new string(req.ReceiverPhone.Where(char.IsDigit).ToArray());
        var shippingAddress = req.ShippingAddress.Trim();
        var paymentMethodCode = string.IsNullOrWhiteSpace(req.PaymentMethodCode)
            ? "COD"
            : req.PaymentMethodCode.Trim().ToUpperInvariant();
        var receiverEmail = string.IsNullOrWhiteSpace(req.ReceiverEmail) ? null : req.ReceiverEmail.Trim();
        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();

        if (string.IsNullOrWhiteSpace(receiverName))
            return CheckoutResult.Fail("Vui lòng nhập họ tên người nhận.");
        if (string.IsNullOrWhiteSpace(receiverPhone) || receiverPhone.Length < 9)
            return CheckoutResult.Fail("Vui lòng nhập số điện thoại người nhận hợp lệ.");
        if (string.IsNullOrWhiteSpace(shippingAddress))
            return CheckoutResult.Fail("Vui lòng nhập địa chỉ giao hàng.");
        if (paymentMethodCode is not ("COD" or "MOMO"))
            return CheckoutResult.Fail("Phương thức thanh toán không hỗ trợ.");

        if (customerId is int cid)
        {
            var exists = await _db.Customers.AnyAsync(x => x.CustomerId == cid && x.IsActive, ct);
            if (!exists) customerId = null;
        }

        var cartItems = _cart.GetCheckoutCart(cartKey);
        if (cartItems.Count == 0)
            return CheckoutResult.Fail("Giỏ hàng trống, chưa thể tạo đơn.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var componentIds = cartItems.Keys.ToList();
            // AsNoTracking: tránh SaveChanges ghi đè StockQty đã trừ atomic
            var components = await _db.Components.AsNoTracking()
                .Where(x => componentIds.Contains(x.ComponentId) && x.IsActive)
                .ToDictionaryAsync(x => x.ComponentId, ct);

            var orderItems = new List<OrderItemEntity>();
            var deductLines = new List<(int ComponentId, int Qty)>();
            decimal subtotal = 0m;

            foreach (var (componentId, qty) in cartItems)
            {
                if (qty <= 0)
                    continue;

                if (!components.TryGetValue(componentId, out var row))
                {
                    await tx.RollbackAsync(ct);
                    return CheckoutResult.Fail($"Sản phẩm #{componentId} không còn bán.");
                }

                if (row.StockQty <= 0 || row.StockQty < qty)
                {
                    await tx.RollbackAsync(ct);
                    return CheckoutResult.Fail(
                        $"{InventoryStockService.InsufficientStockMessage} «{row.Name}» (còn {row.StockQty}, cần {qty}).");
                }

                orderItems.Add(new OrderItemEntity
                {
                    ComponentId = componentId,
                    Qty = qty,
                    UnitPriceVnd = row.PriceVnd
                });
                deductLines.Add((componentId, qty));
                subtotal += row.PriceVnd * qty;
            }

            if (orderItems.Count == 0)
            {
                await tx.RollbackAsync(ct);
                return CheckoutResult.Fail("Không tìm thấy sản phẩm hợp lệ trong giỏ.");
            }

            // Phải khớp 100% số dòng trong giỏ checkout (không bỏ sót dòng thiếu hàng)
            if (orderItems.Count != cartItems.Count(x => x.Value > 0))
            {
                await tx.RollbackAsync(ct);
                return CheckoutResult.Fail(InventoryStockService.InsufficientStockMessage);
            }

            var preview = await _promo.PreviewAsync(subtotal, req.PromoCode, customerId, ct);
            if (!preview.Succeeded)
            {
                await tx.RollbackAsync(ct);
                return CheckoutResult.Fail(preview.Error ?? "Mã khuyến mãi không hợp lệ.");
            }

            var (deductOk, deductError) = await _stock.TryDeductAsync(deductLines, ct);
            if (!deductOk)
            {
                await tx.RollbackAsync(ct);
                return CheckoutResult.Fail(deductError ?? InventoryStockService.InsufficientStockMessage);
            }

            if (preview.PromoCodeId is int promoId)
                await _promo.IncrementUsageAsync(promoId, ct);

            var discountNoteParts = new List<string>();
            if (preview.MembershipDiscountVnd > 0)
                discountNoteParts.Add($"hạng -{preview.MembershipDiscountVnd:N0}đ");
            if (preview.PromoDiscountVnd > 0)
                discountNoteParts.Add($"mã {preview.PromoCode} -{preview.PromoDiscountVnd:N0}đ");
            if (discountNoteParts.Count > 0)
            {
                var discLine = $"[DISCOUNT] {string.Join(", ", discountNoteParts)}";
                note = string.IsNullOrWhiteSpace(note) ? discLine : $"{note}\n{discLine}";
            }

            var status = paymentMethodCode == "MOMO"
                ? OrderStatusCatalog.WaitingPayment
                : OrderStatusCatalog.PendingConfirmation;

            var order = new OrderEntity
            {
                CustomerId = customerId,
                ReceiverName = receiverName,
                ReceiverPhone = receiverPhone,
                ReceiverEmail = receiverEmail,
                ShippingAddress = shippingAddress,
                Note = note,
                PaymentMethodCode = paymentMethodCode,
                TotalPriceVnd = preview.PayableVnd,
                PromoCodeId = preview.PromoCodeId,
                DiscountAmountVnd = preview.TotalDiscountVnd,
                StatusCode = status,
                OrderType = "ONLINE",
                CreatedAtUtc = DateTime.UtcNow,
                Items = orderItems
            };

            _db.Orders.Add(order);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _cart.Clear(cartKey);
            return CheckoutResult.Ok(order.OrderId, paymentMethodCode);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
