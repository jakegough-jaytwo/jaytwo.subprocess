using System;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.Subprocess.Exceptions;

namespace jaytwo.Subprocess
{
    public static class ICliCommandExecutorExtensions
    {
        /// <summary>
        /// Executes with cancellation. Executors that do not implement <see cref="ICancellableCliCommandExecutor"/> cannot honor a token,
        /// so a cancelable token is rejected rather than silently ignored.
        /// </summary>
        public static CliCommandResult Execute(this ICliCommandExecutor executor, CliCommand command, CancellationToken cancellationToken)
        {
            if (executor is ICancellableCliCommandExecutor cancellable)
            {
                return cancellable.Execute(command, cancellationToken);
            }

            ThrowIfCancellationUnsupported(cancellationToken);
            return executor.Execute(command);
        }

        public static Task<CliCommandResult> ExecuteAsync(this ICliCommandExecutor executor, CliCommand command, CancellationToken cancellationToken)
        {
            if (executor is ICancellableCliCommandExecutor cancellable)
            {
                return cancellable.ExecuteAsync(command, cancellationToken);
            }

            ThrowIfCancellationUnsupported(cancellationToken);
            return executor.ExecuteAsync(command);
        }

        public static CliCommandResult Execute(this ICliCommandExecutor executor, CliCommand command, bool suppressCommandLineExceptions)
        {
            CliCommandResult result;

            if (suppressCommandLineExceptions)
            {
                try
                {
                    result = executor.Execute(command);
                }
                catch (SubprocessException ex) when (!ex.Result.Canceled)
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
                catch (SubprocessException ex) when (!ex.Result.Canceled)
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

        private static void ThrowIfCancellationUnsupported(CancellationToken cancellationToken)
        {
            if (cancellationToken.CanBeCanceled)
            {
                throw new NotSupportedException($"This {nameof(ICliCommandExecutor)} does not implement {nameof(ICancellableCliCommandExecutor)}, so it cannot honor a {nameof(CancellationToken)}.");
            }
        }
    }
}
