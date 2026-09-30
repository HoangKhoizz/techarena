namespace ban_link_kien_PC.Domain.Components;

// Composite pattern: treat single components and a whole PC as the same "product" abstraction.
public interface IPriceNode
{
    string DisplayName { get; }
    decimal GetPriceVnd();
}

public sealed class ComponentLeaf : IPriceNode
{
    public ComponentLeaf(int componentId, string displayName, decimal unitPriceVnd, int qty)
    {
        ComponentId = componentId;
        DisplayName = displayName;
        UnitPriceVnd = unitPriceVnd;
        Qty = qty;
    }

    public int ComponentId { get; }
    public string DisplayName { get; }
    public decimal UnitPriceVnd { get; }
    public int Qty { get; }

    public decimal GetPriceVnd() => UnitPriceVnd * Qty;
}

public sealed class CompositeProduct : IPriceNode
{
    private readonly List<IPriceNode> _children = [];

    public CompositeProduct(string displayName) => DisplayName = displayName;

    public string DisplayName { get; }

    public IReadOnlyList<IPriceNode> Children => _children;

    public CompositeProduct Add(IPriceNode node)
    {
        _children.Add(node);
        return this;
    }

    public decimal GetPriceVnd() => _children.Sum(x => x.GetPriceVnd());
}

