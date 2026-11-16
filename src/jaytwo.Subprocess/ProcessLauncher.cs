using System;
using System.Diagnostics;
using System.Threading;

namespace jaytwo.Subprocess;

internal static class ProcessLauncher
{
    internal static void Validate(CliCommand command)
    {
        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (command.StandardInput != null && command.StandardInputStream != null)
        {
            throw new ArgumentException($"Set either {nameof(CliCommand.StandardInput)} or {nameof(CliCommand.StandardInputStream)}, not both.", nameof(command));
        }

        ValidateTimeout(command.Timeout, nameof(CliCommand.Timeout));
        ValidateTimeout(command.OutputDrainTimeout, nameof(CliCommand.OutputDrainTimeout));
    }

    internal static void ValidateTimeout(TimeSpan? value, string name)
    {
        if (value.HasValue && value.Value < TimeSpan.Zero && value.Value != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(name, value, "Must be non-negative or Timeout.InfiniteTimeSpan.");
        }
    }

    internal static ProcessStartInfo CreateStartInfo(ProcessLaunchSpec command, bool redirectInput)
    {
        var processStartInfo = new ProcessStartInfo();
        processStartInfo.UseShellExecute = false; // false if the process should be created directly from the executable file (must be false for redirection)
        processStartInfo.RedirectStandardOutput = true;
        processStartInfo.RedirectStandardError = true;
        processStartInfo.RedirectStandardInput = redirectInput;
        processStartInfo.CreateNoWindow = true;

        if (command.Environment != null)
        {
            foreach (var environmentVariable in command.Environment)
            {
                processStartInfo.Environment[environmentVariable.Key] = environmentVariable.Value;
            }
        }

        processStartInfo.WorkingDirectory = command.WorkingDirectory;
        processStartInfo.FileName = command.FileName;
        processStartInfo.Arguments = command.Arguments;

        if (command.StandardOutputEncoding != null)
        {
            processStartInfo.StandardOutputEncoding = command.StandardOutputEncoding;
        }

        if (command.StandardErrorEncoding != null)
        {
            processStartInfo.StandardErrorEncoding = command.StandardErrorEncoding;
        }

        return processStartInfo;
    }
}
