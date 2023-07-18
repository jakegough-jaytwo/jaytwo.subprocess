using System;
using System.Collections.Generic;
using System.Linq;

namespace jaytwo.Subprocess
{
    public class CliCommand
    {
        public string Arguments { get; set; }

        public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>();

        public int[] ExpectedExitCodes { get; set; } = new int[] { };

        public string FileName { get; set; }

        public TimeSpan? Timeout { get; set; }

        public string WorkingDirectory { get; set; }

        public IList<string> Secrets { get; set; } = new List<string>();

        public override string ToString()
        {
            var result = $"{FileName} {Arguments}".Trim();

            Secrets?.Distinct().ToList().ForEach(x => result = result.Replace(x, "****"));

            return result;
        }
    }
}
