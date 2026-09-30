using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ban_link_kien_PC.Domain.Payments;

public sealed class MoMoOptions
{
    public const string SectionName = "MoMo";

    public string PartnerCode { get; set; } = "MOMO";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Endpoint { get; set; } = "https://test-payment.momo.vn/v2/gateway/api/create";
    public string RefundEndpoint { get; set; } = "https://test-payment.momo.vn/v2/gateway/api/refund";
    public string ReturnUrl { get; set; } = "/api/payments/momo/return";
    public string NotifyUrl { get; set; } = "/api/payments/momo/ipn";
    /// <summary>true = sandbox/demo nội bộ (QR + refund giả lập).</summary>
    public bool UseDemoMode { get; set; } = true;
}

public sealed record CreatePaymentResult(
    bool Succeeded,
    string? PayUrl,
    string? QrCodeUrl,
    string? RequestId,
    string? Error,
    bool IsDemo = false);

public sealed record RefundResult(
    bool Succeeded,
    string? RefundTransId,
    string? Error,
    bool IsDemo = false);

public interface IPaymentGatewayService
{
    Task<CreatePaymentResult> CreatePaymentUrlAsync(
        int orderId,
        decimal totalAmount,
        string orderInfo,
        string publicBaseUrl,
        CancellationToken ct = default);

    Task<RefundResult> RefundTransactionAsync(
        string originalTransactionId,
        decimal amount,
        int orderId,
        string? description = null,
        CancellationToken ct = default);

    bool VerifyIpnSignature(IReadOnlyDictionary<string, string> fields);
}

public sealed class MoMoPaymentGatewayService : IPaymentGatewayService
{
    private readonly MoMoOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MoMoPaymentGatewayService> _logger;

    public MoMoPaymentGatewayService(
        IOptions<MoMoOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<MoMoPaymentGatewayService> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<CreatePaymentResult> CreatePaymentUrlAsync(
        int orderId,
        decimal totalAmount,
        string orderInfo,
        string publicBaseUrl,
        CancellationToken ct = default)
    {
        var amount = (long)Math.Round(totalAmount, 0, MidpointRounding.AwayFromZero);
        if (amount <= 0)
            return new CreatePaymentResult(false, null, null, null, "Số tiền không hợp lệ.");

        var requestId = Guid.NewGuid().ToString("N");
        var orderIdStr = $"TA{orderId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var baseUrl = publicBaseUrl.TrimEnd('/');
        var returnUrl = $"{baseUrl}{_options.ReturnUrl}";
        var notifyUrl = $"{baseUrl}{_options.NotifyUrl}";

        if (_options.UseDemoMode ||
            string.IsNullOrWhiteSpace(_options.SecretKey) ||
            string.IsNullOrWhiteSpace(_options.AccessKey))
        {
            // Demo: payUrl dùng để vẽ QR — KHÔNG tự đánh dấu PAID.
            // Khách quét / bấm "Thanh toán demo" → gọi API confirm.
            var payUrl =
                $"https://test-payment.momo.vn/v2/gateway/pay?partnerCode={Uri.EscapeDataString(_options.PartnerCode)}" +
                $"&orderId={Uri.EscapeDataString(orderIdStr)}" +
                $"&requestId={requestId}" +
                $"&amount={amount}" +
                $"&extraData={orderId}" +
                $"&orderInfo={Uri.EscapeDataString(orderInfo)}";

            return new CreatePaymentResult(true, payUrl, payUrl, requestId, null, IsDemo: true);
        }

        var extraData = orderId.ToString();
        var rawSignature =
            $"accessKey={_options.AccessKey}" +
            $"&amount={amount}" +
            $"&extraData={extraData}" +
            $"&ipnUrl={notifyUrl}" +
            $"&orderId={orderIdStr}" +
            $"&orderInfo={orderInfo}" +
            $"&partnerCode={_options.PartnerCode}" +
            $"&redirectUrl={returnUrl}" +
            $"&requestId={requestId}" +
            $"&requestType=captureWallet";

        var signature = SignHmacSha256(rawSignature, _options.SecretKey);

        var body = new Dictionary<string, object>
        {
            ["partnerCode"] = _options.PartnerCode,
            ["partnerName"] = "Tech Arena",
            ["storeId"] = "TechArenaStore",
            ["requestId"] = requestId,
            ["amount"] = amount,
            ["orderId"] = orderIdStr,
            ["orderInfo"] = orderInfo,
            ["redirectUrl"] = returnUrl,
            ["ipnUrl"] = notifyUrl,
            ["lang"] = "vi",
            ["requestType"] = "captureWallet",
            ["autoCapture"] = true,
            ["extraData"] = extraData,
            ["signature"] = signature
        };

        try
        {
            var client = _httpClientFactory.CreateClient("MoMo");
            using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(_options.Endpoint, content, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            var parsed = JsonSerializer.Deserialize<MoMoCreateResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parsed is null || string.IsNullOrWhiteSpace(parsed.PayUrl))
            {
                _logger.LogWarning("MoMo create failed: {Json}", json);
                return new CreatePaymentResult(false, null, null, requestId, parsed?.Message ?? "Không tạo được link MoMo.");
            }

            // qrCodeUrl từ MoMo (nếu có) — fallback payUrl để frontend tự vẽ QR
            var qr = string.IsNullOrWhiteSpace(parsed.QrCodeUrl) ? parsed.PayUrl : parsed.QrCodeUrl;
            return new CreatePaymentResult(true, parsed.PayUrl, qr, requestId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MoMo CreatePaymentUrl error");
            return new CreatePaymentResult(false, null, null, requestId, ex.Message);
        }
    }

    public async Task<RefundResult> RefundTransactionAsync(
        string originalTransactionId,
        decimal amount,
        int orderId,
        string? description = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(originalTransactionId))
            return new RefundResult(false, null, "Thiếu TransactionId gốc để hoàn tiền.");

        var refundAmount = (long)Math.Round(amount, 0, MidpointRounding.AwayFromZero);
        if (refundAmount <= 0)
            return new RefundResult(false, null, "Số tiền hoàn không hợp lệ.");

        var requestId = Guid.NewGuid().ToString("N");
        var orderIdStr = $"RF{orderId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var desc = string.IsNullOrWhiteSpace(description)
            ? $"Hoan tien don hang #{orderId}"
            : description.Trim();

        if (_options.UseDemoMode ||
            string.IsNullOrWhiteSpace(_options.SecretKey) ||
            string.IsNullOrWhiteSpace(_options.AccessKey))
        {
            var demoRefundId = $"RDEMO{requestId[..10]}";
            _logger.LogInformation(
                "MoMo DEMO refund OK. Order={OrderId}, OriginalTrans={Trans}, RefundId={RefundId}, Amount={Amount}",
                orderId, originalTransactionId, demoRefundId, refundAmount);
            return new RefundResult(true, demoRefundId, null, IsDemo: true);
        }

        var rawSignature =
            $"accessKey={_options.AccessKey}" +
            $"&amount={refundAmount}" +
            $"&description={desc}" +
            $"&orderId={orderIdStr}" +
            $"&partnerCode={_options.PartnerCode}" +
            $"&requestId={requestId}" +
            $"&transId={originalTransactionId}";

        var signature = SignHmacSha256(rawSignature, _options.SecretKey);
        var body = new Dictionary<string, object>
        {
            ["partnerCode"] = _options.PartnerCode,
            ["orderId"] = orderIdStr,
            ["requestId"] = requestId,
            ["amount"] = refundAmount,
            ["transId"] = long.TryParse(originalTransactionId, out var tid) ? tid : originalTransactionId,
            ["lang"] = "vi",
            ["description"] = desc,
            ["signature"] = signature
        };

        try
        {
            var client = _httpClientFactory.CreateClient("MoMo");
            using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(_options.RefundEndpoint, content, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            var parsed = JsonSerializer.Deserialize<MoMoRefundResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parsed is null || parsed.ResultCode != 0)
            {
                _logger.LogWarning("MoMo refund failed: {Json}", json);
                return new RefundResult(false, null, parsed?.Message ?? "Hoàn tiền MoMo thất bại.");
            }

            return new RefundResult(true, parsed.TransId?.ToString() ?? requestId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MoMo RefundTransaction error");
            return new RefundResult(false, null, ex.Message);
        }
    }

    public bool VerifyIpnSignature(IReadOnlyDictionary<string, string> fields)
    {
        if (_options.UseDemoMode)
            return true;

        if (!fields.TryGetValue("signature", out var signature) || string.IsNullOrWhiteSpace(signature))
            return false;

        var raw = string.Join("&", fields
            .Where(kv => !string.Equals(kv.Key, "signature", StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));

        var expected = SignHmacSha256(raw, _options.SecretKey);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }

    private static string SignHmacSha256(string message, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(message);
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(data)).ToLowerInvariant();
    }

    private sealed class MoMoCreateResponse
    {
        [JsonPropertyName("payUrl")]
        public string? PayUrl { get; set; }

        [JsonPropertyName("qrCodeUrl")]
        public string? QrCodeUrl { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("resultCode")]
        public int ResultCode { get; set; }
    }

    private sealed class MoMoRefundResponse
    {
        [JsonPropertyName("resultCode")]
        public int ResultCode { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("transId")]
        public long? TransId { get; set; }
    }
}
