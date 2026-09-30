using System.Text.Json;
using ban_link_kien_PC.Domain.Chat;
using Microsoft.AspNetCore.Mvc;

namespace ban_link_kien_PC.Controllers.Api;

public sealed record ChatRequest(string Message);

[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IGeminiChatService _gemini;
    public ChatController(IGeminiChatService gemini) => _gemini = gemini;

    [HttpPost]
    public async Task Send([FromBody] ChatRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { reply = "Bạn hãy nhập tin nhắn trước nhé." }, ct);
            return;
        }

        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers.Connection = "keep-alive";
        Response.Headers.Append("X-Accel-Buffering", "no");

        var buffering = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
        buffering?.DisableBuffering();

        var hasContent = false;

        try
        {
            await foreach (var chunk in _gemini.StreamMessageAsync(req.Message.Trim(), ct))
            {
                hasContent = true;
                await WriteSseEventAsync(new { text = chunk }, ct);
            }

            if (!hasContent)
                await WriteSseEventAsync(new { error = "Xin lỗi, mình chưa có câu trả lời cho câu hỏi này." }, ct);

            await Response.WriteAsync("data: [DONE]\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Chat SSE ERROR] {ex}");
            await WriteSseEventAsync(new
            {
                error = "Xin lỗi, trợ lý AI đang gặp sự cố. Bạn vui lòng thử lại sau nhé!"
            }, ct);
            await Response.WriteAsync("data: [DONE]\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    private async Task WriteSseEventAsync(object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await Response.WriteAsync($"data: {json}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
