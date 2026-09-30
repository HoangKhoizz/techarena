namespace ban_link_kien_PC.Domain.Shipping;

public sealed class ShippingCreateRequest
{
    public int OrderId { get; init; }
    public string ProviderCode { get; init; } = "";
    public string ReceiverName { get; init; } = "";
    public string ReceiverPhone { get; init; } = "";
    public string ShippingAddress { get; init; } = "";
    public decimal CodAmountVnd { get; init; }
}

public sealed class ShippingCreateResult
{
    public bool Ok { get; init; }
    public string? TrackingNumber { get; init; }
    public string? ProviderCode { get; init; }
    public string? Error { get; init; }

    public static ShippingCreateResult Success(string trackingNumber, string providerCode) => new()
    {
        Ok = true,
        TrackingNumber = trackingNumber,
        ProviderCode = providerCode
    };

    public static ShippingCreateResult Fail(string error) => new()
    {
        Ok = false,
        Error = error
    };
}

/// <summary>Giả lập / tích hợp API đơn vị vận chuyển (GHTK, Viettel Post…).</summary>
public interface IShippingService
{
    Task<ShippingCreateResult> CreateShipmentAsync(ShippingCreateRequest request, CancellationToken ct = default);
}

/// <summary>API vận chuyển lỗi / bảo trì.</summary>
public sealed class ShippingApiException : Exception
{
    public ShippingApiException(string message) : base(message) { }
}
