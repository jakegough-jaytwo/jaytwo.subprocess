using System.Threading;
using System.Threading.Tasks;

namespace jaytwo.Subprocess;

/// <summary>
/// Added alongside <see cref="ICliCommandExecutor"/> (rather than changing it) so existing implementers keep compiling.
/// </summary>
public interface ICancellableCliCommandExecutor : ICliCommandExecutor
{
    CliCommandResult Execute(CliCommand command, CancellationToken cancellationToken);

    Task<CliCommandResult> ExecuteAsync(CliCommand command, CancellationToken cancellationToken);
}
