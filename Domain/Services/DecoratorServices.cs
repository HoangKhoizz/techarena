namespace ban_link_kien_PC.Domain.Services;

// Decorator pattern: add optional services without exploding subclasses.
public interface IServicePricing
{
    string Name { get; }
    decimal AddOnPriceVnd { get; }
}

public sealed class BaseBuildService : IServicePricing
{
    public string Name => "Base build service";
    public decimal AddOnPriceVnd => 0m;
}

public abstract class ServiceDecorator : IServicePricing
{
    protected ServiceDecorator(IServicePricing inner) => Inner = inner;
    protected IServicePricing Inner { get; }
    public abstract string Name { get; }
    public abstract decimal AddOnPriceVnd { get; }
}

public sealed class OverclockServiceDecorator : ServiceDecorator
{
    public OverclockServiceDecorator(IServicePricing inner) : base(inner) { }
    public override string Name => $"{Inner.Name} + Overclock";
    public override decimal AddOnPriceVnd => Inner.AddOnPriceVnd + 250_000m;
}

public sealed class ExtendedWarrantyDecorator : ServiceDecorator
{
    public ExtendedWarrantyDecorator(IServicePricing inner) : base(inner) { }
    public override string Name => $"{Inner.Name} + Extended warranty";
    public override decimal AddOnPriceVnd => Inner.AddOnPriceVnd + 350_000m;
}

