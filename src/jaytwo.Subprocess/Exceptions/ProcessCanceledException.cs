using System;
using System.Threading;

namespace jaytwo.Subprocess.Exceptions;

/// <summary>
/// The caller's <see cref="CancellationToken"/> was canceled and the process tree was terminated.
/// Deliberately not a <see cref="SubprocessException"/>, so <c>suppressCommandLineExceptions</c> never swallows a cancellation.
/// </summary>
public class ProcessCanceledException : OperationCanceledException
{
    public ProcessCanceledException(CliCommandResult result, CancellationToken cancellationToken)
        : base("Command was canceled", cancellationToken)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public CliCommandResult Result { get; }
}
