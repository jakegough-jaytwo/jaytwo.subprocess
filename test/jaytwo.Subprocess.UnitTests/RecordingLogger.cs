using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace jaytwo.Subprocess.UnitTests;

internal sealed class RecordingLogger : ILogger
{
    public List<string> Messages { get; } = new List<string>();

    public Action<string>? OnLog { get; set; }

    public bool Enabled { get; set; } = true;

    IDisposable ILogger.BeginScope<TState>(TState state) => null!;

    public bool IsEnabled(LogLevel logLevel) => Enabled;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        Messages.Add(message);
        OnLog?.Invoke(message);
    }
}
