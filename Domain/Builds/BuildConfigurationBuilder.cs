namespace ban_link_kien_PC.Domain.Builds;

// Builder pattern: build a complex configuration step-by-step.
public sealed class BuildConfigurationBuilder
{
    private readonly List<SelectedComponent> _selected = [];
    private string _name = "My build";

    public BuildConfigurationBuilder Named(string name)
    {
        _name = string.IsNullOrWhiteSpace(name) ? "My build" : name.Trim();
        return this;
    }

    public BuildConfigurationBuilder AddCpu(int componentId) => Add("CPU", componentId, 1);
    public BuildConfigurationBuilder AddMainboard(int componentId) => Add("MAINBOARD", componentId, 1);
    public BuildConfigurationBuilder AddRam(int componentId, int qty) => Add("RAM", componentId, qty);
    public BuildConfigurationBuilder AddGpu(int componentId) => Add("GPU", componentId, 1);
    public BuildConfigurationBuilder AddSsd(int componentId) => Add("SSD", componentId, 1);
    public BuildConfigurationBuilder AddPsu(int componentId) => Add("PSU", componentId, 1);

    public BuildConfigurationBuilder Add(string categoryCode, int componentId, int qty)
    {
        _selected.RemoveAll(x => x.CategoryCode == categoryCode);
        _selected.Add(new SelectedComponent(componentId, categoryCode, Math.Max(1, qty)));
        return this;
    }

    public ComputerConfiguration Build()
        => new()
        {
            Name = _name,
            Selected = _selected.ToArray()
        };
}

