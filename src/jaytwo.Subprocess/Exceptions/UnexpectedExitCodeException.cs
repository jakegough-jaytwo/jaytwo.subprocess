using System;
using System.Collections.Generic;
using System.Text;

namespace jaytwo.Subprocess.Exceptions
{
    public class UnexpectedExitCodeException : SubprocessException
    {
        public UnexpectedExitCodeException(CliCommandResult result)
            : base(result, GetMessage(result))
        {
        }

        private static string GetMessage(CliCommandResult result)
        {
            return $"Unexpected Exit Code: {result.ExitCode}";
        }
    }
}
