using System.Collections.Generic;

namespace SpriteEditXL.Model;

/// <summary>Deep-clone-based undo stack keyed by workspace snapshots, matching js/undo.js.</summary>
public sealed class UndoStack
{
    private readonly LinkedList<Workspace> _undos = new();
    private readonly Stack<Workspace> _redos = new();
    private readonly int _max;

    public UndoStack(int maxLevels) => _max = maxLevels;

    public bool CanUndo => _undos.Count > 0;
    public bool CanRedo => _redos.Count > 0;

    public void Push(Workspace snapshot)
    {
        _undos.AddLast(snapshot.Clone());
        while (_undos.Count > _max) _undos.RemoveFirst();
        _redos.Clear();
    }

    public Workspace? Undo(Workspace current)
    {
        if (_undos.Count == 0) return null;
        var prior = _undos.Last!.Value;
        _undos.RemoveLast();
        _redos.Push(current.Clone());
        return prior;
    }

    public Workspace? Redo(Workspace current)
    {
        if (_redos.Count == 0) return null;
        _undos.AddLast(current.Clone());
        return _redos.Pop();
    }

    public void Clear() { _undos.Clear(); _redos.Clear(); }
}
