using System.Threading.Tasks;
using jaytwo.Subprocess.Exceptions;

namespace jaytwo.Subprocess
{
    public static class ICliCommandExecutorExtensions
    {
        public static CliCommandResult Execute(this ICliCommandExecutor executor, CliCommand command, bool suppressCommandLineExceptions)
        {
            CliCommandResult result;

            if (suppressCommandLineExceptions)
            {
                try
                {
                    result = executor.Execute(command);
                }
                catch (SubprocessException ex)
                {
                    result = ex.Result;
                }
            }
            else
            {
                result = executor.Execute(command);
            }

            return result;
        }

        public static async Task<CliCommandResult> ExecuteAsync(this ICliCommandExecutor executor, CliCommand command, bool suppressCommandLineExceptions)
        {
            CliCommandResult result;

            if (suppressCommandLineExceptions)
            {
                try
                {
                    result = await executor.ExecuteAsync(command);
                }
                catch (SubprocessException ex)
                {
                    result = ex.Result;
                }
            }
            else
            {
                result = await executor.ExecuteAsync(command);
            }

            return result;
        }
    }
}
