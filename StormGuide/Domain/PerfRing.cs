namespace StormGuide.Domain;

/// <summary>
/// Bounded ring of <see cref="double"/> samples (frame-cost in milliseconds, by
/// convention). Computes <see cref="P50"/> and <see cref="P95"/> across the
/// most-recent <see cref="Capacity"/> samples.
///
/// Pure: no Unity / BepInEx / game refs so it sits in <c>Domain/</c> and is
/// unit-tested alongside the rest of the layer. The Diagnostics tab reads
/// these values for the per-section frame-cost breakdown.
/// </summary>
public sealed class PerfRing
{
    private readonly Queue<double> _samples;

    public int Capacity { get; }

    public PerfRing(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be positive");
        Capacity = capacity;
        _samples = new Queue<double>(capacity);
    }

    public int Count => _samples.Count;

    /// <summary>
    /// Append a sample; the oldest sample is dropped once the ring is full.
    /// </summary>
    public void Push(double sample)
    {
        _samples.Enqueue(sample);
        while (_samples.Count > Capacity) _samples.Dequeue();
    }

    /// <summary>Median across the current ring contents. Returns 0 when empty.</summary>
    public double P50 => Percentile(0.5);

    /// <summary>p95 across the current ring contents. Returns 0 when empty.</summary>
    public double P95 => Percentile(0.95);

    /// <summary>
    /// Nearest-rank percentile across the current ring contents. Returns 0
    /// when the ring is empty.
    /// </summary>
    public double Percentile(double p)
    {
        // !(p >= 0 && p <= 1) rejects NaN too: NaN comparisons are all false.
        if (!(p >= 0 && p <= 1))
            throw new ArgumentOutOfRangeException(nameof(p), "percentile must be between 0 and 1");
        if (_samples.Count == 0) return 0;
        var arr = _samples.ToArray();
        Array.Sort(arr);
        var idx = Math.Min(arr.Length - 1, (int)Math.Floor((arr.Length - 1) * p));
        return arr[idx];
    }
}
