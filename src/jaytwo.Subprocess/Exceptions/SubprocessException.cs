using System;
using System.Collections.Generic;
using System.Text;

namespace jaytwo.Subprocess.Exceptions
{
    public class SubprocessException : Exception
    {
        public SubprocessException(CliCommandResult result, string message)
            : base(message)
        {
            Result = result;
        }

        public CliCommandResult Result { get; }
    }
}
