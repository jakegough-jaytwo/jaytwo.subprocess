using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace jaytwo.Subprocess
{
    public class CliCommand
    {
        public string? Arguments { get; set; }

        public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>();

        public int[] ExpectedExitCodes { get; set; } = new int[] { };

        public string? FileName { get; set; }

        public TimeSpan? Timeout { get; set; }

        public string? WorkingDirectory { get; set; }

        public IList<string> Secrets { get; set; } = new List<string>();

        /// <summary>
        /// Text written to the child's stdin, which is then closed. Null (with no <see cref="StandardInputStream"/>) leaves stdin
        /// un-redirected: the child inherits the host's stdin. Use an empty string to give the child immediate EOF.
        /// </summary>
        public string? StandardInput { get; set; }

        /// <summary>
        /// Bytes copied to the child's stdin, from the stream's current position to its end, after which stdin is closed.
        /// The caller owns the stream: it is never disposed or rewound, and must not be used by the caller until execution returns.
        /// Mutually exclusive with <see cref="StandardInput"/>.
        /// </summary>
        public Stream? StandardInputStream { get; set; }

        /// <summary>
        /// Encoding for <see cref="StandardInput"/> text. Default is UTF-8 without a byte order mark. Not used for <see cref="StandardInputStream"/>.
        /// </summary>
        public Encoding? StandardInputEncoding { get; set; }

        /// <summary>
        /// Encoding used to decode stdout. Null keeps the platform default of System.Diagnostics.Process.
        /// </summary>
        public Encoding? StandardOutputEncoding { get; set; }

        /// <summary>
        /// Encoding used to decode stderr. Null keeps the platform default of System.Diagnostics.Process.
        /// </summary>
        public Encoding? StandardErrorEncoding { get; set; }

        /// <summary>
        /// How long to wait, after the process has ended (or been terminated), for stdout/stderr/stdin to finish. Default is 5 seconds.
        /// This bounds the wait when a descendant still holds the redirected pipe handles.
        /// </summary>
        public TimeSpan? OutputDrainTimeout { get; set; }

        public override string ToString() => MaskSecrets($"{FileName} {Arguments}".Trim());

        internal string MaskSecrets(string? value) => ProcessLaunchSpec.MaskSecrets(Secrets, value);

        internal ProcessLaunchSpec ToLaunchSpec() => new ProcessLaunchSpec
        {
            FileName = FileName,
            Arguments = Arguments,
            WorkingDirectory = WorkingDirectory,
            Environment = Environment,
            Secrets = Secrets,
            StandardOutputEncoding = StandardOutputEncoding,
            StandardErrorEncoding = StandardErrorEncoding,
        };
    }
}
