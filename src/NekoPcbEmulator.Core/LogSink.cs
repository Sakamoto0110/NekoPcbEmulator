using System.Collections.Concurrent;

namespace NekoPcbEmulator.Core;

public enum LogLevel
{
    Info,
    Rx,
    Tx,
    Warn,
    Error,
}

public readonly record struct LogEntry(DateTime Timestamp, string Source, LogLevel Level, string Message);

/// <summary>
/// A bounded, lock-free log buffer. Devices write from their socket threads; the UI drains
/// it from a timer. Deliberately not an event: it keeps the core free of any cross-thread
/// marshalling concerns and lets the UI batch updates.
/// </summary>
public sealed class LogSink
{
    private readonly ConcurrentQueue<LogEntry> _queue = new();
    private readonly int _capacity;

    public LogSink(int capacity = 4096) => _capacity = capacity;

    public void Write(string source, LogLevel level, string message)
    {
        _queue.Enqueue(new LogEntry(DateTime.Now, source, level, message));
        while (_queue.Count > _capacity && _queue.TryDequeue(out _))
        {
            // Drop oldest.
        }
    }

    /// <summary>Removes and returns up to <paramref name="max"/> entries, oldest first.</summary>
    public List<LogEntry> Drain(int max = 256)
    {
        var result = new List<LogEntry>();
        while (result.Count < max && _queue.TryDequeue(out var entry))
            result.Add(entry);
        return result;
    }
}
