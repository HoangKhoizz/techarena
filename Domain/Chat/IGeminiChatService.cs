namespace ban_link_kien_PC.Domain.Chat;

public interface IGeminiChatService
{
    /// <summary>
    /// Gửi tin nhắn tới Gemini và trả về từng đoạn text (chunk) theo luồng streaming.
    /// </summary>
    IAsyncEnumerable<string> StreamMessageAsync(string userMessage, CancellationToken ct = default);
}
