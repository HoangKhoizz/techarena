using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ban_link_kien_PC.Domain.Chat;

namespace ban_link_kien_PC.Infrastructure.Gemini;

/// <summary>
/// Gọi Google Gemini (Generative Language API) qua REST streaming (SSE).
/// API key được đọc từ biến môi trường GEMINI_API_KEY (nạp từ file .env).
/// </summary>
public sealed class GeminiChatService : IGeminiChatService
{
    private const string SystemInstruction =
        "Bạn là trợ lý AI thông minh trên website bán PC Tech Arena. " +
        "Bạn có thể trả lời mọi câu hỏi của người dùng một cách thân thiện, " +
        "nhưng nếu có cơ hội, hãy khéo léo liên hệ đến việc mua sắm máy tính hoặc linh kiện PC. " +
        "Luôn trả lời bằng tiếng Việt, ngắn gọn và đúng trọng tâm tư vấn bán hàng.";

    private const int MaxOutputTokens = 250;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<GeminiChatService> _logger;
    private readonly string _apiKey;
    private readonly string _model;

    public GeminiChatService(HttpClient http, IConfiguration config, ILogger<GeminiChatService> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["GEMINI_API_KEY"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
        // Mặc định gemini-1.5-flash (model flash nhẹ). Có thể override qua GEMINI_MODEL trong .env.
        // Nếu Google trả 404, đổi .env sang gemini-flash-lite-latest (bản lite nhẹ nhất còn hoạt động).
        _model = config["GEMINI_MODEL"] ?? Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-1.5-flash";
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("Chưa cấu hình GEMINI_API_KEY. Vui lòng kiểm tra file .env.");

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:streamGenerateContent?alt=sse";

        var body = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = SystemInstruction } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userMessage } }
                }
            },
            generationConfig = new
            {
                maxOutputTokens = MaxOutputTokens
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-goog-api-key", _apiKey);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            Console.WriteLine($"[Gemini ERROR] HTTP {(int)response.StatusCode} - model={_model}");
            Console.WriteLine($"[Gemini ERROR] Body: {errorBody}");
            _logger.LogWarning("Gemini stream API trả về lỗi {Status}: {Body}", (int)response.StatusCode, errorBody);
            throw new HttpRequestException($"Gemini API error {(int)response.StatusCode}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                break;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var payload = line["data: ".Length..].Trim();
            if (payload.Length == 0 || payload == "[DONE]")
                break;

            var chunk = ExtractText(payload);
            if (!string.IsNullOrEmpty(chunk))
                yield return chunk;
        }
    }

    /// <summary>
    /// Trích text từ một chunk JSON của Gemini stream.
    /// </summary>
    private static string ExtractText(string json)
    {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.GetArrayLength() == 0)
            return "";

        if (!candidates[0].TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts))
            return "";

        var sb = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var t))
                sb.Append(t.GetString());
        }
        return sb.ToString();
    }
}
