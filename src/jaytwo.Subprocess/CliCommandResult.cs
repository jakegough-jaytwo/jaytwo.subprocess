using System;

namespace jaytwo.Subprocess
{
    public class CliCommandResult
    {
        public CliCommand Command { get; set; }

        public TimeSpan Duration { get; set; }

        public int ExitCode { get; set; }

        public string StandardError { get; set; }

        public string StandardOutput { get; set; }

        public bool Success { get; set; }

        public bool TimedOut { get; set; }

        public override string ToString() => string.Format("{0} ({1:n0} ms) {2}", TimedOut ? "TIMEOUT" : Success ? "OK" : $"FAIL (exit code {ExitCode})", Duration.TotalMilliseconds, Command);
    }
}
