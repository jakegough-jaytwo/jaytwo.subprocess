using System.Threading.Tasks;

namespace jaytwo.Subprocess
{
    public static class CliCommandExtensions
    {
        public static CliCommandResult Execute(this CliCommand command)
        {
            return new CliCommandExecutor().Execute(command);
        }

        public static CliCommandResult Execute(this CliCommand command, ICliCommandExecutor executor)
        {
            return executor.Execute(command);
        }

        public static CliCommandResult Execute(this CliCommand command, bool suppressCommandLineExceptions)
        {
            return new CliCommandExecutor().Execute(command, suppressCommandLineExceptions);
        }

        public static CliCommandResult Execute(this CliCommand command, bool suppressCommandLineExceptions, ICliCommandExecutor executor)
        {
            return executor.Execute(command, suppressCommandLineExceptions);
        }

        public static Task<CliCommandResult> ExecuteAsync(this CliCommand command)
        {
            return new CliCommandExecutor().ExecuteAsync(command);
        }

        public static Task<CliCommandResult> ExecuteAsync(this CliCommand command, ICliCommandExecutor executor)
        {
            return executor.ExecuteAsync(command);
        }

        public static Task<CliCommandResult> ExecuteAsync(this CliCommand command, bool suppressCommandLineExceptions)
        {
            return new CliCommandExecutor().ExecuteAsync(command, suppressCommandLineExceptions);
        }

        public static Task<CliCommandResult> ExecuteAsync(this CliCommand command, bool suppressCommandLineExceptions, ICliCommandExecutor executor)
        {
            return executor.ExecuteAsync(command, suppressCommandLineExceptions);
        }
    }
}
