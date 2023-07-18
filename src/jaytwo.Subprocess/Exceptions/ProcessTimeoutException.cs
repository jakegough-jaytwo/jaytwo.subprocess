using System;
using System.Collections.Generic;
using System.Text;

namespace jaytwo.Subprocess.Exceptions
{
    public class ProcessTimeoutException : SubprocessException
    {
        public ProcessTimeoutException(CliCommandResult result)
            : base(result, GetMessage(result))
        {
        }

        private static string GetMessage(CliCommandResult result)
        {
            return $"Command timed out";
        }
    }
}
