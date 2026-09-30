using ban_link_kien_PC.Domain.Components;

namespace ban_link_kien_PC.Domain.Builds;

public sealed record SelectedComponent(int ComponentId, string CategoryCode, int Qty);

public sealed class ComputerConfiguration
{
    public required string Name { get; init; }
    public IReadOnlyList<SelectedComponent> Selected { get; init; } = [];

    // Composite view for pricing.
    public CompositeProduct AsComposite(string displayName, Func<int, (string name, decimal priceVnd)> resolver)
    {
        var pc = new CompositeProduct(displayName);
        foreach (var s in Selected)
        {
            var info = resolver(s.ComponentId);
            pc.Add(new ComponentLeaf(s.ComponentId, $"{s.CategoryCode}: {info.name}", info.priceVnd, s.Qty));
        }
        return pc;
    }
}

