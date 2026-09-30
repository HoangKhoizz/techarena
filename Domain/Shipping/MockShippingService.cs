namespace ban_link_kien_PC.Domain.Shipping;

/// <summary>
/// Mock API GHTK / Viettel Post.
/// ~10% request quăng <see cref="ShippingApiException"/> để test lỗi bảo trì.
/// Giao hỏa tốc (shipper cửa hàng) luôn thành công, không gọi API ngoài.
/// </summary>
public sealed class MockShippingService : IShippingService
{
    /// <summary>Xác suất lỗi API giả lập (0.10 = 10%).</summary>
    public const double FailureRate = 0.10;

    public async Task<ShippingCreateResult> CreateShipmentAsync(
        ShippingCreateRequest request,
        CancellationToken ct = default)
    {
        var provider = ShippingProviderCatalog.Normalize(request.ProviderCode);
        if (!ShippingProviderCatalog.IsKnown(provider))
            return ShippingCreateResult.Fail("Đơn vị vận chuyển không hợp lệ.");

        // Giả lập độ trễ mạng
        await Task.Delay(Random.Shared.Next(80, 220), ct);

        // Shipper cửa hàng: không gọi API ngoài
        if (provider == ShippingProviderCatalog.ExpressStore)
        {
            var localTracking = BuildTracking(provider, request.OrderId);
            return ShippingCreateResult.Success(localTracking, provider);
        }

        // Giả lập API GHTK / Viettel Post — 10% bảo trì
        if (Random.Shared.NextDouble() < FailureRate)
            throw new ShippingApiException("API bên vận chuyển đang bảo trì.");

        var tracking = BuildTracking(provider, request.OrderId);
        return ShippingCreateResult.Success(tracking, provider);
    }

    private static string BuildTracking(string provider, int orderId)
    {
        var prefix = ShippingProviderCatalog.TrackingPrefix(provider);
        var stamp = DateTime.UtcNow.ToString("yyMMddHHmm");
        var suffix = Random.Shared.Next(100000, 999999);
        return $"{prefix}-{orderId}-{stamp}-{suffix}";
    }
}
