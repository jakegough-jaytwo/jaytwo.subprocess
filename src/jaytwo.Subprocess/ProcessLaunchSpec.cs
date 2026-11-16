using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace jaytwo.Subprocess;

/// <summary>
/// The launch-only settings shared by the one-shot path (<see cref="CliCommand"/>) and the session path
/// (<see cref="Streaming.CliProcessOptions"/>). Everything that starts, drains or terminates a process goes through this.
/// </summary>
internal sealed class ProcessLaunchSpec
{
    public string? FileName { get; set; }

    public string? Arguments { get; set; }

    public string? WorkingDirectory { get; set; }

    public IDictionary<string, string>? Environment { get; set; }

    public IList<string>? Secrets { get; set; }

    public Encoding? StandardOutputEncoding { get; set; }

    public Encoding? StandardErrorEncoding { get; set; }

    public static string MaskSecrets(IEnumerable<string>? secrets, string? value)
    {
        var result = value ?? string.Empty;

        if (secrets != null)
        {
            foreach (var secret in secrets.Where(x => !string.IsNullOrEmpty(x)).Distinct())
            {
                result = result.Replace(secret, "****");
            }
        }

        return result;
    }
}
