using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace StormGuide.Data;

/// <summary>
/// Bounded ring-buffer log listener that captures lines emitted by the plugin's
/// own <c>BepInEx</c> log source. The Diagnostics tab renders the captured
/// lines so a player can self-diagnose without alt-tabbing to <c>LogOutput.log</c>.
/// </summary>
internal sealed class LogCapture : ILogListener
{
    public sealed record Entry(DateTime UtcAt, LogLevel Level, string Source, string Message);

    private readonly LinkedList<Entry> _entries = new();
    private readonly int               _capacity;
    private readonly string            _filterSource;
    private readonly object            _lock = new();

    public LogCapture(string filterSource, int capacity = 200)
    {
        _filterSource = filterSource;
        _capacity     = Math.Max(16, capacity);
    }

    public IReadOnlyList<Entry> Snapshot()
    {
        lock (_lock) return _entries.ToArray();
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
    }

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        try
        {
            // Only capture our own plugin's lines so the buffer stays focused.
            if (eventArgs?.Source?.SourceName == null) return;
            if (!string.Equals(eventArgs.Source.SourceName, _filterSource,
                    StringComparison.Ordinal)) return;

            var entry = new Entry(
                UtcAt:   DateTime.UtcNow,
                Level:   eventArgs.Level,
                Source:  eventArgs.Source.SourceName,
                Message: eventArgs.Data?.ToString() ?? "");

            lock (_lock)
            {
                _entries.AddLast(entry);
                while (_entries.Count > _capacity) _entries.RemoveFirst();
            }
        }
        catch { /* listener swallow: never throw out of the log pipeline. */ }
    }

    public void Dispose() { /* no-op */ }
}
