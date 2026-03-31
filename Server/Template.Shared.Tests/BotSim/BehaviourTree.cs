using System;
using System.Collections.Generic;

namespace Template.Shared.Tests;

public enum BtStatus { Success, Failure, Running }

public abstract class BtNode
{
    public abstract BtStatus Tick(BtBlackboard bb);
    public virtual void Reset() { }
}

/// <summary>Simple key-value store for passing data between behaviour tree nodes.</summary>
public class BtBlackboard
{
    private readonly Dictionary<string, object> _data = new();

    public T Get<T>(string key) => _data.TryGetValue(key, out var v) && v is T t ? t : default;
    public void Set(string key, object value) { if (value == null) _data.Remove(key); else _data[key] = value; }
    public bool Has(string key) => _data.ContainsKey(key);
}

/// <summary>Runs children in order. Returns Failure on first child Failure, Success when all succeed.
/// Resumes from a Running child on the next tick.</summary>
public class Sequence : BtNode
{
    private readonly BtNode[] _children;
    private int _index;

    public Sequence(params BtNode[] children) => _children = children;

    public override BtStatus Tick(BtBlackboard bb)
    {
        while (_index < _children.Length)
        {
            var s = _children[_index].Tick(bb);
            if (s == BtStatus.Running) return BtStatus.Running;
            if (s == BtStatus.Failure) { Reset(); return BtStatus.Failure; }
            _index++;
        }
        Reset();
        return BtStatus.Success;
    }

    public override void Reset()
    {
        foreach (var c in _children) c.Reset();
        _index = 0;
    }
}

/// <summary>Tries children in order. Returns Success on first child Success, Failure when all fail.
/// Resumes from a Running child on the next tick.</summary>
public class Selector : BtNode
{
    private readonly BtNode[] _children;
    private int _index;

    public Selector(params BtNode[] children) => _children = children;

    public override BtStatus Tick(BtBlackboard bb)
    {
        while (_index < _children.Length)
        {
            var s = _children[_index].Tick(bb);
            if (s == BtStatus.Running) return BtStatus.Running;
            if (s == BtStatus.Success) { Reset(); return BtStatus.Success; }
            _index++;
        }
        Reset();
        return BtStatus.Failure;
    }

    public override void Reset()
    {
        foreach (var c in _children) c.Reset();
        _index = 0;
    }
}

/// <summary>Evaluates a predicate. Returns Success if true, Failure if false.</summary>
public class If : BtNode
{
    private readonly Func<BtBlackboard, bool> _predicate;
    public If(Func<BtBlackboard, bool> predicate) => _predicate = predicate;
    public override BtStatus Tick(BtBlackboard bb) => _predicate(bb) ? BtStatus.Success : BtStatus.Failure;
}

/// <summary>Executes an action function that returns a BtStatus.</summary>
public class Do : BtNode
{
    private readonly Func<BtBlackboard, BtStatus> _fn;
    public Do(Func<BtBlackboard, BtStatus> fn) => _fn = fn;
    public override BtStatus Tick(BtBlackboard bb) => _fn(bb);
}

/// <summary>Repeats its child forever. Resets the child on completion and always returns Running.</summary>
public class RepeatForever : BtNode
{
    private readonly BtNode _child;
    public RepeatForever(BtNode child) => _child = child;

    public override BtStatus Tick(BtBlackboard bb)
    {
        var s = _child.Tick(bb);
        if (s != BtStatus.Running) _child.Reset();
        return BtStatus.Running;
    }

    public override void Reset() => _child.Reset();
}

/// <summary>Returns Running for a number of ticks, then Success.</summary>
public class Wait : BtNode
{
    private readonly int _ticks;
    private int _remaining = -1;

    public Wait(int ticks) => _ticks = ticks;

    public override BtStatus Tick(BtBlackboard bb)
    {
        if (_remaining < 0) _remaining = _ticks;
        return --_remaining <= 0 ? BtStatus.Success : BtStatus.Running;
    }

    public override void Reset() => _remaining = -1;
}
