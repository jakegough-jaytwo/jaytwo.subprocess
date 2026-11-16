using System;

namespace jaytwo.Subprocess
{
    public class CliCommandResult
    {
        public CliCommand? Command { get; set; }

        public TimeSpan Duration { get; set; }

        public int ExitCode { get; set; }

        public string? StandardError { get; set; }

        public string? StandardOutput { get; set; }

        public bool Success { get; set; }

        public bool TimedOut { get; set; }

        /// <summary>
        /// True when the caller's cancellation token terminated the process tree.
        /// </summary>
        public bool Canceled { get; set; }

        /// <summary>
        /// True when a stream did not reach end-of-file within <see cref="CliCommand.OutputDrainTimeout"/> after the process ended
        /// (typically a descendant still holding the redirected pipes), so <see cref="StandardOutput"/>/<see cref="StandardError"/> may be missing trailing text.
        /// </summary>
        public bool OutputIncomplete { get; set; }

        /// <summary>
        /// True when termination was attempted (timeout or cancellation) but the process had not exited within
        /// <see cref="CliCommand.OutputDrainTimeout"/>. The process may still be running; <see cref="ExitCode"/> is not meaningful,
        /// and the captured text is whatever had been read so far.
        /// </summary>
        public bool TerminationFailed { get; set; }

        public int? ProcessId { get; set; }

        public override string ToString() => string.Format("{0} ({1:n0} ms) {2}", TerminationFailed ? "TERMINATION FAILED" : Canceled ? "CANCELED" : TimedOut ? "TIMEOUT" : Success ? "OK" : $"FAIL (exit code {ExitCode})", Duration.TotalMilliseconds, Command);
    }
}
