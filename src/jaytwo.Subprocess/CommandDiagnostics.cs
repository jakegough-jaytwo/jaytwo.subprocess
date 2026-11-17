using System;
using Microsoft.Extensions.Logging;

namespace jaytwo.Subprocess;

internal sealed class CommandDiagnostics
{
    private readonly ILogger? _logger;
    private readonly string[] _secrets;
    private readonly string? _workingDirectory;

    internal CommandDiagnostics(CliCommand command)
    {
        _logger = command.Logger;
        _secrets = command.Secrets == null ? Array.Empty<string>() : new System.Collections.Generic.List<string>(command.Secrets).ToArray();
        _workingDirectory = command.WorkingDirectory;
        Command = command.ToString();
    }

    internal string Command { get; }

    internal void LogCommand(string loggerId)
    {
        Log(loggerId, "Command", Command);
        if (!string.IsNullOrWhiteSpace(_workingDirectory))
        {
            Log(loggerId, "WorkingDirectory", _workingDirectory);
        }
    }

    internal void LogResult(string loggerId, CliCommandResult result)
    {
        try
        {
            var phrase = result.TerminationFailed ? "TERMINATION FAILED" : result.Canceled ? "CANCELED" : result.TimedOut ? "TIMEOUT" : result.Success ? "OK" : "FAIL";
            Log(loggerId, "Result", $"{phrase} (Duration: {result.Duration.TotalMilliseconds:n0} ms; Exit Code: {result.ExitCode})");
            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                Log(loggerId, nameof(result.StandardOutput), result.StandardOutput);
            }

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                Log(loggerId, nameof(result.StandardError), result.StandardError);
            }
        }
        catch
        {
            // Diagnostics must never replace the command outcome.
        }
    }

    internal void LogStarted(int processId) => Log(processId.ToString(), "Started", Command);

    internal void LogTermination(int processId, bool hasExited)
    {
        try
        {
            Log(processId.ToString(), "Termination", hasExited ? "EXITED" : "TERMINATION FAILED");
        }
        catch
        {
            // Diagnostics must never interrupt termination or disposal.
        }
    }

    private void Log(string loggerId, string name, string? value)
    {
        if (_logger == null || !_logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        const int maxLength = 2048;
        var text = ProcessLaunchSpec.MaskSecrets(_secrets, value).Trim();
        var disclaimer = string.Empty;
        if (text.Length == 0)
        {
            text = "(empty)";
        }
        else if (text.Length > maxLength)
        {
            disclaimer = $" (truncated length to {maxLength}; originally {text.Length}) ";
            text = text.Substring(0, maxLength);
        }

        _logger.LogInformation($"{loggerId} {name}{disclaimer}: {text}");
    }
}
