namespace ban_link_kien_PC.Domain.Builds;

// Command pattern: enable undo/redo for build editor.
public interface IBuildCommand
{
    string Name { get; }
    void Execute(BuildDraft draft);
    void Undo(BuildDraft draft);
}

public sealed class BuildDraft
{
    // categoryCode -> (componentId, qty)
    private readonly Dictionary<string, (int componentId, int qty)> _selected = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, (int componentId, int qty)> Selected => _selected;

    public (bool hasValue, (int componentId, int qty) value) TryGet(string categoryCode)
        => _selected.TryGetValue(categoryCode, out var v) ? (true, v) : (false, default);

    public void Set(string categoryCode, int componentId, int qty)
        => _selected[categoryCode] = (componentId, Math.Max(1, qty));

    public void Remove(string categoryCode) => _selected.Remove(categoryCode);

    public void Clear() => _selected.Clear();
}

public sealed class SelectComponentCommand : IBuildCommand
{
    private readonly string _categoryCode;
    private readonly int _componentId;
    private readonly int _qty;
    private (bool hasPrev, (int componentId, int qty) prev) _prev;

    public SelectComponentCommand(string categoryCode, int componentId, int qty)
    {
        _categoryCode = categoryCode;
        _componentId = componentId;
        _qty = qty;
    }

    public string Name => $"Select {_categoryCode}";

    public void Execute(BuildDraft draft)
    {
        _prev = draft.TryGet(_categoryCode);
        draft.Set(_categoryCode, _componentId, _qty);
    }

    public void Undo(BuildDraft draft)
    {
        if (_prev.hasPrev) draft.Set(_categoryCode, _prev.prev.componentId, _prev.prev.qty);
        else draft.Remove(_categoryCode);
    }
}

public sealed class RemoveCategoryCommand : IBuildCommand
{
    private readonly string _categoryCode;
    private (bool hasPrev, (int componentId, int qty) prev) _prev;

    public RemoveCategoryCommand(string categoryCode) => _categoryCode = categoryCode;
    public string Name => $"Remove {_categoryCode}";

    public void Execute(BuildDraft draft)
    {
        _prev = draft.TryGet(_categoryCode);
        draft.Remove(_categoryCode);
    }

    public void Undo(BuildDraft draft)
    {
        if (_prev.hasPrev) draft.Set(_categoryCode, _prev.prev.componentId, _prev.prev.qty);
    }
}

public sealed class BuildCommandHistory
{
    private readonly Stack<IBuildCommand> _undo = new();
    private readonly Stack<IBuildCommand> _redo = new();

    public void Execute(IBuildCommand cmd, BuildDraft draft)
    {
        cmd.Execute(draft);
        _undo.Push(cmd);
        _redo.Clear();
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Undo(BuildDraft draft)
    {
        if (_undo.Count == 0) return;
        var cmd = _undo.Pop();
        cmd.Undo(draft);
        _redo.Push(cmd);
    }

    public void Redo(BuildDraft draft)
    {
        if (_redo.Count == 0) return;
        var cmd = _redo.Pop();
        cmd.Execute(draft);
        _undo.Push(cmd);
    }

    public void Reset()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

