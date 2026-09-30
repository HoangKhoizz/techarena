using System.Collections.Concurrent;

namespace ban_link_kien_PC.Domain.Notifications;

public sealed record UserNotificationItem(
    string Title,
    string Message,
    string? Url,
    string? ImageUrl,
    DateTime CreatedAtUtc);

public interface IUserNotificationCenter
{
    void Push(int userId, string title, string message, string? url = null, string? imageUrl = null);
    IReadOnlyList<UserNotificationItem> GetLatest(int userId, int take = 8);
    int GetUnreadCount(int userId);
    void ClearAll(int userId);
}

public sealed class InMemoryUserNotificationCenter : IUserNotificationCenter
{
    private const int MaxPerUser = 30;
    private readonly ConcurrentDictionary<int, ConcurrentQueue<UserNotificationItem>> _store = new();

    public void Push(int userId, string title, string message, string? url = null, string? imageUrl = null)
    {
        if (userId <= 0) return;

        var queue = _store.GetOrAdd(userId, _ => new ConcurrentQueue<UserNotificationItem>());
        queue.Enqueue(new UserNotificationItem(title, message, url, imageUrl, DateTime.UtcNow));

        while (queue.Count > MaxPerUser)
            queue.TryDequeue(out _);
    }

    public IReadOnlyList<UserNotificationItem> GetLatest(int userId, int take = 8)
    {
        if (userId <= 0 || take <= 0) return [];
        if (!_store.TryGetValue(userId, out var queue)) return [];

        return queue.ToArray()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(take)
            .ToList();
    }

    public int GetUnreadCount(int userId)
    {
        if (userId <= 0) return 0;
        if (!_store.TryGetValue(userId, out var queue)) return 0;
        return queue.Count;
    }

    public void ClearAll(int userId)
    {
        if (userId <= 0) return;
        _store.TryRemove(userId, out _);
    }
}

