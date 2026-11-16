using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.Subprocess.Exceptions;
using Microsoft.Extensions.Logging;

namespace jaytwo.Subprocess
{
    public class CliCommandExecutor : ICancellableCliCommandExecutor
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly ILogger? _logger;

        public CliCommandExecutor()
            : this(default(ILogger<ICliCommandExecutor>))
        {
        }

        public CliCommandExecutor(ILogger<ICliCommandExecutor>? logger)
        {
            _logger = logger;
        }

        private static TimeSpan DefaultTimeout { get; } = TimeSpan.FromSeconds(60);

        private static TimeSpan DefaultOutputDrainTimeout { get; } = TimeSpan.FromSeconds(5);

        private static int[] DefaultExpectedExitCodes { get; } = new[] { 0 };

        public CliCommandResult Execute(CliCommand command) => Execute(command, CancellationToken.None);

        public CliCommandResult Execute(CliCommand command, CancellationToken cancellationToken)
        {
            // runs the same async core as ExecuteAsync so both paths have identical semantics; Task.Run avoids sync-context deadlocks
            return Task.Run(() => InnerExecute(command, cancellationToken), CancellationToken.None).GetAwaiter().GetResult();
        }

        public Task<CliCommandResult> ExecuteAsync(CliCommand command) => ExecuteAsync(command, CancellationToken.None);

        public Task<CliCommandResult> ExecuteAsync(CliCommand command, CancellationToken cancellationToken) => InnerExecute(command, cancellationToken);

        // best effort and bounded: the original exception is what the caller needs to see
        private static async Task CleanUpAfterFailureAsync(Process process, Task exited, CancellationTokenSource inputCancellation, TimeSpan drainTimeout)
        {
            try
            {
                ProcessTree.TryTerminate(process);
                await WaitAsync(exited, drainTimeout).ConfigureAwait(false);
                inputCancellation.Cancel();
            }
            catch
            {
            }
        }

        private static async Task<bool> WaitAsync(Task task, TimeSpan timeout)
        {
            if (task.IsCompleted)
            {
                return true;
            }

            if (timeout == Timeout.InfiniteTimeSpan)
            {
                await task.ConfigureAwait(false);
                return true;
            }

            using (var delayCancellation = new CancellationTokenSource())
            {
                var winner = await Task.WhenAny(task, Task.Delay(timeout, delayCancellation.Token)).ConfigureAwait(false);
                if (winner == task)
                {
                    delayCancellation.Cancel();
                    return true;
                }

                return false;
            }
        }

        // Feeds stdin concurrently with output draining and always closes it so EOF-dependent children can finish.
        // A child that exits or closes stdin early (broken pipe) is not an error; a failing caller stream is.
        private static async Task FeedInputAsync(StreamWriter stdin, CliCommand command, StopSignal stop, CancellationToken cancellationToken)
        {
            try
            {
                var pipe = stdin.BaseStream;
                var source = command.StandardInputStream;

                if (source == null)
                {
                    var bytes = (command.StandardInputEncoding ?? Utf8NoBom).GetBytes(command.StandardInput ?? string.Empty);
                    await pipe.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var buffer = new byte[81920];

                    while (true)
                    {
                        int read;

                        try
                        {
                            read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            stop.Fail(ex);
                            return;
                        }

                        if (read == 0)
                        {
                            break;
                        }

                        await pipe.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                    }
                }

                await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // the child closed stdin or exited before reading everything
            }
            catch (ObjectDisposedException)
            {
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                try
                {
                    stdin.Close();
                }
                catch (Exception)
                {
                }
            }
        }

        // with wisdom from https://stackoverflow.com/questions/1145969/processinfo-and-redirectstandardoutput
        private async Task<CliCommandResult> InnerExecute(CliCommand command, CancellationToken cancellationToken)
        {
            ProcessLauncher.Validate(command);
            ProcessTree.EnsureSupported();
            cancellationToken.ThrowIfCancellationRequested();

            var timeout = command.Timeout ?? DefaultTimeout;
            var drainTimeout = command.OutputDrainTimeout ?? DefaultOutputDrainTimeout;
            var redirectInput = command.StandardInput != null || command.StandardInputStream != null;

            var loggerId = new string(Guid.NewGuid().ToString().Replace("-", string.Empty).Take(10).ToArray());
            LogCommand(loggerId, command);
            var spec = command.ToLaunchSpec();

            var stop = new StopSignal();
            int exitCode;
            int processId;
            bool terminated;
            bool drained;
            bool terminationFailed;
            string standardOutput;
            string standardError;
            TimeSpan duration;

            var stopwatch = Stopwatch.StartNew();

            using (var process = new Process { StartInfo = ProcessLauncher.CreateStartInfo(spec, redirectInput), EnableRaisingEvents = true })
            using (var timeoutSource = new CancellationTokenSource())
            using (var inputCancellation = new CancellationTokenSource())
            {
                var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.Exited += (sender, e) => exited.TrySetResult(true);

                // a launch failure throws here, before anything exists that needs cleanup
                process.Start();
                processId = process.Id;

                var stdout = new OutputPump(process.StandardOutput);
                var stderr = new OutputPump(process.StandardError);
                stdout.Start();
                stderr.Start();

                var input = Task.CompletedTask;
                if (redirectInput)
                {
                    var stdin = process.StandardInput;
                    var inputToken = inputCancellation.Token;
                    input = Task.Run(() => FeedInputAsync(stdin, command, stop, inputToken));
                }

                try
                {
                    using (cancellationToken.Register(() => stop.Request(StopSignal.Canceled)))
                    {
                        if (timeout != Timeout.InfiniteTimeSpan)
                        {
                            timeoutSource.CancelAfter(timeout);
                        }

                        using (timeoutSource.Token.Register(() => stop.Request(StopSignal.TimedOut)))
                        {
                            await Task.WhenAny(exited.Task, stop.Stopped).ConfigureAwait(false);
                        }
                    }

                    // if the process exited on its own first (or at the same moment) that outcome stands; a stop request only wins if we actually terminate it
                    terminated = !exited.Task.IsCompleted && stop.Reason != StopSignal.None && ProcessTree.TryTerminate(process);

                    terminationFailed = !await WaitAsync(exited.Task, drainTimeout).ConfigureAwait(false);

                    // the child is gone (or will not go): stop feeding it, then give the readers a bounded chance to reach EOF
                    inputCancellation.Cancel();
                    drained = !terminationFailed
                        && await WaitAsync(Task.WhenAll(stdout.Completion, stderr.Completion, input), drainTimeout).ConfigureAwait(false);

                    standardOutput = stdout.GetText();
                    standardError = stderr.GetText();
                    drained = drained && !stdout.Failed && !stderr.Failed;
                    exitCode = terminationFailed ? 0 : process.ExitCode;
                }
                catch
                {
                    await CleanUpAfterFailureAsync(process, exited.Task, inputCancellation, drainTimeout).ConfigureAwait(false);
                    throw;
                }
            }

            stopwatch.Stop();
            duration = stopwatch.Elapsed;

            // a child that would not die outranks the input error: the caller needs the result to find it
            if (stop.InputError != null && !terminationFailed)
            {
                ExceptionDispatchInfo.Capture(stop.InputError).Throw();
            }

            var result = new CliCommandResult();
            result.Command = command;
            result.Duration = duration;
            result.StandardError = standardError;
            result.StandardOutput = standardOutput;
            result.ExitCode = exitCode;
            result.ProcessId = processId;
            result.TimedOut = (terminated || terminationFailed) && stop.Reason == StopSignal.TimedOut;
            result.Canceled = (terminated || terminationFailed) && stop.Reason == StopSignal.Canceled;
            result.OutputIncomplete = !drained;
            result.TerminationFailed = terminationFailed;

            var expectedExitCodes = (command.ExpectedExitCodes ?? new int[] { }).Any()
                ? command.ExpectedExitCodes
                : DefaultExpectedExitCodes;

            result.Success = !result.TimedOut && !result.Canceled && !result.TerminationFailed && expectedExitCodes!.Contains(exitCode);

            LogResult(loggerId, result);

            if (terminationFailed)
            {
                throw new ProcessTerminationException(result);
            }

            if (result.Canceled)
            {
                throw new ProcessCanceledException(result, cancellationToken);
            }

            if (result.TimedOut)
            {
                throw new ProcessTimeoutException(result);
            }

            if (!result.Success)
            {
                throw new UnexpectedExitCodeException(result);
            }

            return result;
        }

        private void LogCommand(string loggerId, CliCommand command)
        {
            LogLongString(loggerId, "Command", command.ToString());

            if (!string.IsNullOrWhiteSpace(command.WorkingDirectory))
            {
                LogLongString(loggerId, nameof(command.WorkingDirectory), command.WorkingDirectory);
            }
        }

        private void LogResult(string loggerId, CliCommandResult result)
        {
            var resultPhrase = result.TerminationFailed ? "TERMINATION FAILED" : result.Canceled ? "CANCELED" : result.TimedOut ? "TIMEOUT" : result.Success ? "OK" : "FAIL";

            LogLongString(loggerId, "Result", $"{resultPhrase} (Duration: {result.Duration.TotalMilliseconds:n0} ms; Exit Code: {result.ExitCode})");

            // output can echo secrets, so it is masked the same way the command line is
            var standardOutput = result.Command?.MaskSecrets(result.StandardOutput) ?? result.StandardOutput;
            var standardError = result.Command?.MaskSecrets(result.StandardError) ?? result.StandardError;

            if (!string.IsNullOrWhiteSpace(standardOutput))
            {
                LogLongString(loggerId, nameof(result.StandardOutput), standardOutput);
            }

            if (!string.IsNullOrWhiteSpace(standardError))
            {
                LogLongString(loggerId, nameof(result.StandardError), standardError);
            }
        }

        private void LogLongString(string loggerId, string name, string? value)
        {
            var maxLogLength = 2048;

            var valueToLog = value?.Trim() ?? string.Empty;
            var disclaimer = string.Empty;

            if (string.IsNullOrEmpty(valueToLog))
            {
                valueToLog = "(empty)";
            }
            else if (valueToLog.Length > maxLogLength)
            {
                valueToLog = new string(valueToLog.Take(maxLogLength).ToArray());
                disclaimer = $" (truncated length to {maxLogLength}; originally {value!.Length}) ";
            }

            _logger?.LogInformation($"{loggerId} {name}{disclaimer}: {valueToLog}");
        }

        private sealed class StopSignal
        {
            internal const int None = 0;
            internal const int TimedOut = 1;
            internal const int Canceled = 2;
            internal const int InputFailed = 3;

            private readonly TaskCompletionSource<bool> _stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _reason;

            public Task Stopped => _stopped.Task;

            public int Reason => Volatile.Read(ref _reason);

            public Exception? InputError { get; private set; }

            public void Request(int reason)
            {
                Interlocked.CompareExchange(ref _reason, reason, None);
                _stopped.TrySetResult(true);
            }

            public void Fail(Exception inputError)
            {
                InputError = inputError;
                Request(InputFailed);
            }
        }
    }
}
