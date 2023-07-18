using System;
using System.Collections.Generic;
using System.Text;

namespace jaytwo.Subprocess.Exceptions
{
    public class TimeoutException : SubprocessException
    {
        public TimeoutException(CliCommandResult result)
            : base(result, GetMessage(result))
        {
        }

        private static string GetMessage(CliCommandResult result)
        {
            return $"Command timed out.\n\nStandard Error: {result.StandardError}\n\nStandard Output: {result.StandardOutput}";
        }
    }
}
