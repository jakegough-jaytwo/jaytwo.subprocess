namespace jaytwo.Subprocess.Exceptions;

/// <summary>
/// A timeout or cancellation terminated the process tree, but the process had not exited within
/// <see cref="CliCommand.OutputDrainTimeout"/>. <see cref="SubprocessException.Result"/> carries the partial output and
/// has <see cref="CliCommandResult.TerminationFailed"/> set; the process may still be running.
/// </summary>
public class ProcessTerminationException : SubprocessException
{
    public ProcessTerminationException(CliCommandResult result)
        : base(result, "Process did not exit after it was terminated")
    {
    }
}
