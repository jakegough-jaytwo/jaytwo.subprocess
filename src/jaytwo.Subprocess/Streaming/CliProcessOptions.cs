using System;
using System.Collections.Generic;
using System.Text;

namespace jaytwo.Subprocess.Streaming;

/// <summary>
/// Launch and terminate settings for a <see cref="CliProcessSession"/>. Deliberately has no timeout, expected exit codes or
/// one-shot stdin: a session is driven through its streams, and when it ends is the caller's decision.
/// </summary>
public class CliProcessOptions
{
    public CliProcessOptions()
    {
    }

    public CliProcessOptions(string fileName, string? arguments = null)
    {
        FileName = fileName;
        Arguments = arguments;
    }

    public string? FileName { get; set; }

    /// <summary>
    /// The already-quoted command line. Prefer <see cref="CliCommandBuilder.GetProcessOptions"/> so quoting and launch settings stay consistent with one-shot runs.
    /// </summary>
    public string? Arguments { get; set; }

    public string? WorkingDirectory { get; set; }

    public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>();

    /// <summary>
    /// Values masked as <c>****</c> in <see cref="ToString"/>.
    /// </summary>
    public IList<string> Secrets { get; set; } = new List<string>();

    /// <summary>
    /// Encoding of the session's <see cref="CliProcessSession.StandardInput"/> writer. Default is UTF-8 without a byte order mark.
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
    /// How long <see cref="CliProcessSession.Terminate"/> and Dispose wait for the process to exit after killing its tree. Default is 5 seconds.
    /// </summary>
    public TimeSpan? TerminateTimeout { get; set; }

    public override string ToString() => ProcessLaunchSpec.MaskSecrets(Secrets, $"{FileName} {Arguments}".Trim());

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
