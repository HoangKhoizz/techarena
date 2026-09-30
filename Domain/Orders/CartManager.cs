namespace ban_link_kien_PC.Domain.Orders;

// Singleton pattern: 1 cart manager per application instance (demo purpose).
// In production you'd usually scope it per user/session and persist to DB/Redis.
public sealed class CartManager
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<CartCardState>> _sessionCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _checkoutSelection = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CartCardSnapshot> GetCards(string sessionKey)
    {
        lock (_lock)
        {
            if (!_sessionCards.TryGetValue(sessionKey, out var cards))
                return [];

            return cards.Select(CloneSnapshot).ToList();
        }
    }

    public IReadOnlyDictionary<int, int> GetCart(string sessionKey)
    {
        lock (_lock)
        {
            return BuildCartInternal(sessionKey, null);
        }
    }

    public void SetCheckoutSelection(string sessionKey, IReadOnlyList<string> selectedCardIds)
    {
        lock (_lock)
        {
            if (!_sessionCards.TryGetValue(sessionKey, out var cards))
            {
                _checkoutSelection.Remove(sessionKey);
                return;
            }

            var allowed = cards.Select(x => x.CardId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var normalized = selectedCardIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => allowed.Contains(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (normalized.Count == 0)
                _checkoutSelection.Remove(sessionKey);
            else
                _checkoutSelection[sessionKey] = normalized;
        }
    }

    public IReadOnlyCollection<string> GetCheckoutSelection(string sessionKey)
    {
        lock (_lock)
        {
            if (_checkoutSelection.TryGetValue(sessionKey, out var selected))
                return selected.ToList();
            return [];
        }
    }

    public IReadOnlyDictionary<int, int> GetCheckoutCart(string sessionKey)
    {
        lock (_lock)
        {
            _checkoutSelection.TryGetValue(sessionKey, out var selected);
            return BuildCartInternal(sessionKey, selected);
        }
    }

    public string AddComponentCard(
        string sessionKey,
        int componentId,
        int qty = 1,
        string cardType = "COMPONENT",
        string? title = null,
        bool asSeparateCard = false)
    {
        lock (_lock)
        {
            var cards = GetOrCreateCards(sessionKey);
            var normalizedQty = Math.Max(1, qty);
            var normalizedType = string.IsNullOrWhiteSpace(cardType) ? "COMPONENT" : cardType.Trim().ToUpperInvariant();

            if (!asSeparateCard)
            {
                var existing = cards.FirstOrDefault(x => x.CardType == normalizedType && x.Items.Count == 1 && x.Items[0].ComponentId == componentId);
                if (existing is not null)
                {
                    existing.Items[0].Qty = normalizedQty;
                    if (!string.IsNullOrWhiteSpace(title))
                        existing.Title = title.Trim();
                    return existing.CardId;
                }
            }

            var card = new CartCardState
            {
                CardId = $"card-{Guid.NewGuid():N}",
                CardType = normalizedType,
                Title = title?.Trim() ?? string.Empty,
                Items = [new CartCardItemState { ComponentId = componentId, Qty = normalizedQty }]
            };
            cards.Add(card);
            return card.CardId;
        }
    }

    public string AddBuildCard(string sessionKey, IReadOnlyList<BuildCartItemInput> items, string? title = null)
    {
        lock (_lock)
        {
            var normalized = items
                .Where(x => x.ComponentId > 0 && x.Qty > 0)
                .GroupBy(x => x.ComponentId)
                .Select(g => new CartCardItemState
                {
                    ComponentId = g.Key,
                    Qty = g.Sum(x => Math.Max(1, x.Qty))
                })
                .ToList();

            if (normalized.Count == 0)
                return string.Empty;

            var cards = GetOrCreateCards(sessionKey);
            var card = new CartCardState
            {
                CardId = $"build-{Guid.NewGuid():N}",
                CardType = "BUILD_PC",
                Title = string.IsNullOrWhiteSpace(title) ? "Cấu hình PC tự build" : title.Trim(),
                Items = normalized
            };
            cards.Add(card);
            return card.CardId;
        }
    }

    public void AddOrUpdate(string sessionKey, int componentId, int qty)
    {
        AddComponentCard(sessionKey, componentId, qty, "COMPONENT", null, asSeparateCard: false);
    }

    public void Remove(string sessionKey, int componentId)
    {
        lock (_lock)
        {
            if (!_sessionCards.TryGetValue(sessionKey, out var cards))
                return;

            foreach (var card in cards.ToList())
            {
                card.Items.RemoveAll(x => x.ComponentId == componentId);
                if (card.Items.Count == 0)
                {
                    cards.Remove(card);
                    if (_checkoutSelection.TryGetValue(sessionKey, out var selected))
                        selected.Remove(card.CardId);
                }
            }
        }
    }

    public void RemoveCard(string sessionKey, string cardId)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(cardId))
                return;

            if (_sessionCards.TryGetValue(sessionKey, out var cards))
            {
                cards.RemoveAll(x => string.Equals(x.CardId, cardId, StringComparison.OrdinalIgnoreCase));
                if (_checkoutSelection.TryGetValue(sessionKey, out var selected))
                    selected.Remove(cardId);
            }
        }
    }

    public void Clear(string sessionKey)
    {
        lock (_lock)
        {
            _sessionCards.Remove(sessionKey);
            _checkoutSelection.Remove(sessionKey);
        }
    }

    private List<CartCardState> GetOrCreateCards(string sessionKey)
    {
        if (!_sessionCards.TryGetValue(sessionKey, out var cards))
        {
            cards = [];
            _sessionCards[sessionKey] = cards;
        }

        return cards;
    }

    private Dictionary<int, int> BuildCartInternal(string sessionKey, HashSet<string>? selectedCardIds)
    {
        if (!_sessionCards.TryGetValue(sessionKey, out var cards))
            return new Dictionary<int, int>();

        var result = new Dictionary<int, int>();
        foreach (var card in cards)
        {
            if (selectedCardIds is not null && !selectedCardIds.Contains(card.CardId))
                continue;

            foreach (var item in card.Items)
            {
                if (result.TryGetValue(item.ComponentId, out var oldQty))
                    result[item.ComponentId] = oldQty + item.Qty;
                else
                    result[item.ComponentId] = item.Qty;
            }
        }

        return result;
    }

    private static CartCardSnapshot CloneSnapshot(CartCardState x) => new(
        x.CardId,
        x.CardType,
        x.Title,
        x.Items.Select(i => new CartCardItemSnapshot(i.ComponentId, i.Qty)).ToList());

    private sealed class CartCardState
    {
        public string CardId { get; set; } = "";
        public string CardType { get; set; } = "COMPONENT";
        public string Title { get; set; } = "";
        public List<CartCardItemState> Items { get; set; } = [];
    }

    private sealed class CartCardItemState
    {
        public int ComponentId { get; set; }
        public int Qty { get; set; }
    }
}

public sealed record BuildCartItemInput(int ComponentId, int Qty);
public sealed record CartCardItemSnapshot(int ComponentId, int Qty);
public sealed record CartCardSnapshot(
    string CardId,
    string CardType,
    string Title,
    IReadOnlyList<CartCardItemSnapshot> Items);

