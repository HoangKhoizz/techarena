using System.Collections.Concurrent;

namespace ban_link_kien_PC.Domain.Cskh;

/// <summary>Rate limit gửi hỏi đáp: 1 câu / key / cửa sổ thời gian.</summary>
public sealed class QuestionRateLimiter
{
    private readonly ConcurrentDictionary<string, DateTime> _lastSubmitUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _window;

    public QuestionRateLimiter(TimeSpan? window = null)
    {
        _window = window ?? TimeSpan.FromMinutes(2);
    }

    public bool TryAcquire(string key, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        if (string.IsNullOrWhiteSpace(key))
            key = "anonymous";

        var now = DateTime.UtcNow;
        if (_lastSubmitUtc.TryGetValue(key, out var last))
        {
            var elapsed = now - last;
            if (elapsed < _window)
            {
                retryAfterSeconds = (int)Math.Ceiling((_window - elapsed).TotalSeconds);
                return false;
            }
        }

        _lastSubmitUtc[key] = now;
        Cleanup(now);
        return true;
    }

    private void Cleanup(DateTime now)
    {
        if (_lastSubmitUtc.Count < 500) return;
        foreach (var kv in _lastSubmitUtc)
        {
            if (now - kv.Value > _window * 2)
                _lastSubmitUtc.TryRemove(kv.Key, out _);
        }
    }
}
