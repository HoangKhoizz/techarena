namespace ban_link_kien_PC.Domain.Factories;

// Factory Method pattern: create domain "component requests" from category codes.
public interface IComponentRequest
{
    string CategoryCode { get; }
    int ComponentId { get; }
    int Qty { get; }
}

public sealed record ComponentRequest(string CategoryCode, int ComponentId, int Qty) : IComponentRequest;

public abstract class ComponentRequestCreator
{
    public IComponentRequest Create(string categoryCode, int componentId, int qty)
    {
        if (string.IsNullOrWhiteSpace(categoryCode)) throw new ArgumentException("categoryCode is required");
        return CreateCore(categoryCode.Trim().ToUpperInvariant(), componentId, qty);
    }

    protected abstract IComponentRequest CreateCore(string categoryCode, int componentId, int qty);
}

public sealed class DefaultComponentRequestCreator : ComponentRequestCreator
{
    protected override IComponentRequest CreateCore(string categoryCode, int componentId, int qty)
        => new ComponentRequest(categoryCode, componentId, Math.Max(1, qty));
}

// Abstract Factory pattern: create a brand ecosystem "family" selection strategy.
public interface IEcosystemFactory
{
    string Code { get; }
    string Name { get; }
    string PreferredBrand { get; }
    IReadOnlyList<EcosystemCategoryRule> CategoryRules { get; }
}

public sealed record EcosystemCategoryRule(string CategoryCode, int Qty = 1);

public sealed class AsusEcosystemFactory : IEcosystemFactory
{
    public string Code => "ASUS";
    public string Name => "ASUS Ecosystem";
    public string PreferredBrand => "ASUS";
    public IReadOnlyList<EcosystemCategoryRule> CategoryRules =>
    [
        new("MAINBOARD"),
        new("GPU"),
        new("PSU"),
        new("CASE")
    ];
}

public sealed class MsiEcosystemFactory : IEcosystemFactory
{
    public string Code => "MSI";
    public string Name => "MSI Ecosystem";
    public string PreferredBrand => "MSI";
    public IReadOnlyList<EcosystemCategoryRule> CategoryRules =>
    [
        new("MAINBOARD"),
        new("GPU"),
        new("PSU"),
        new("CASE")
    ];
}

public sealed class AmdPerformanceFactory : IEcosystemFactory
{
    public string Code => "AMD_PERF";
    public string Name => "AMD Performance";
    public string PreferredBrand => "AMD";
    public IReadOnlyList<EcosystemCategoryRule> CategoryRules =>
    [
        new("CPU"),
        new("GPU"),
        new("MAINBOARD"),
        new("RAM", 1),
        new("PSU")
    ];
}

