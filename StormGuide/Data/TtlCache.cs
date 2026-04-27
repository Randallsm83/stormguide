using System;
using UnityEngine;

namespace StormGuide.Data;

/// <summary>
/// Single-slot cache that re-evaluates its producer at most once every
/// <see cref="ttlSeconds"/>. Used to keep IMGUI redraws cheap when the
/// underlying data (settlement state) only changes a few times a second.
/// Not thread-safe; intended for the Unity main thread.
/// </summary>
internal sealed class TtlCache<T>
{
    private readonly Func<T> _producer;
    private readonly float   _ttlSeconds;
    private T?    _value;
    private float _expiresAt;

    public TtlCache(Func<T> producer, float ttlSeconds)
    {
        _producer   = producer;
        _ttlSeconds = ttlSeconds;
        _expiresAt  = float.MinValue;
    }

    public T Get()
    {
        var now = Time.realtimeSinceStartup;
        if (now >= _expiresAt)
        {
            _value     = _producer();
            _expiresAt = now + _ttlSeconds;
        }
        return _value!;
    }

    /// <summary>Force the next <see cref="Get"/> to recompute.</summary>
    public void Invalidate() => _expiresAt = float.MinValue;
}
