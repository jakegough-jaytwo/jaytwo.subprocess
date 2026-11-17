using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace jaytwo.Subprocess.Streaming;

/// <summary>
/// A started process whose stdin/stdout stay open for the caller, for long-lived workers that speak a request/response protocol.
/// This is only the primitive: it does no framing, pooling, restarting or per-request timeouts.
///
/// Ownership: the session owns the process, its pipes and the stderr drain; <see cref="Dispose"/> terminates the whole
/// process tree if it is still running and releases everything. <see cref="StandardInput"/> and <see cref="StandardOutput"/>
/// are borrowed from the session and must not be used after Dispose.
///
/// This is the escape hatch for live streaming; for "run a tool and get its output" use <see cref="CliCommand"/>.
/// Start through <see cref="CliCommand.Start()"/> or <see cref="CliCommand.Start(Action{CliCommandBuilder})"/>.
/// </summary>
public sealed class CliProcessSession : IDisposable
{
    private const int MaxStandardErrorLength = 64 * 1024;

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly TimeSpan DefaultTerminateTimeout = TimeSpan.FromSeconds(5);

    private readonly Process _process;
    private readonly OutputPump _standardErrorPump;
    private readonly TaskCompletionSource<bool> _exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan _terminateTimeout;
    private readonly CommandDiagnostics _diagnostics;
    private int _disposed;

    private CliProcessSession(Process process, CliCommand command, CommandDiagnostics diagnostics)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _terminateTimeout = command.TerminateTimeout ?? DefaultTerminateTimeout;

        ProcessId = process.Id;
        // LF: line protocols (bash/node/python workers) want \n even on Windows; Environment.NewLine would embed \r.
        StandardInput = new StreamWriter(process.StandardInput.BaseStream, command.StandardInputEncoding ?? Utf8NoBom)
        {
            AutoFlush = true,
            NewLine = "\n",
        };
        StandardOutput = process.StandardOutput;

        // an undrained stderr pipe can block the worker once it fills, so the session always drains it (keeping only the tail)
        _standardErrorPump = new OutputPump(process.StandardError, MaxStandardErrorLength);
        _standardErrorPump.Start();
    }

    public int ProcessId { get; }

    /// <summary>
    /// The masked launch line captured before start.
    /// </summary>
    public string Command => _diagnostics.Command;

    /// <summary>
    /// The live process, for telemetry such as sampling memory. Borrowed: do not dispose it or use its redirected streams.
    /// </summary>
    public Process Process => _process;

    /// <summary>
    /// Writes to the worker's stdin. AutoFlush is on and the encoding is <see cref="CliCommand.StandardInputEncoding"/> (default UTF-8, no BOM).
    /// <c>NewLine</c> defaults to <c>\n</c> (LF) for cross-platform line protocols; set it after start if you need CRLF.
    /// Disposing it closes stdin (EOF for the worker).
    /// </summary>
    public StreamWriter StandardInput { get; }

    /// <summary>
    /// Reads the worker's stdout, decoded with <see cref="CliCommand.StandardOutputEncoding"/>. Returns null/0 at end of stream.
    /// A pending read cannot be cancelled; call <see cref="Terminate"/> to unblock it.
    /// </summary>
    public StreamReader StandardOutput { get; }

    /// <summary>
    /// The most recent stderr text (at most the last 64K characters), drained continuously in the background.
    /// </summary>
    public string StandardError => _standardErrorPump.GetText();

    public bool HasExited => _exited.Task.IsCompleted;

    /// <summary>
    /// Waits for the process to exit and returns its exit code. Cancelling the token only abandons the wait (throwing
    /// <see cref="OperationCanceledException"/>); it does not terminate the process. Use <see cref="Terminate"/> for that.
    /// </summary>
    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        if (!_exited.Task.IsCompleted)
        {
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (cancellationToken.Register(() => canceled.TrySetResult(true)))
            {
                if (await Task.WhenAny(_exited.Task, canceled.Task).ConfigureAwait(false) != _exited.Task)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }

        ThrowIfDisposed();
        return _process.ExitCode;
    }

    /// <summary>
    /// Terminates the process and all of its descendants, then waits (bounded by <see cref="CliCommand.TerminateTimeout"/>) for it to exit.
    /// Safe to call repeatedly, from any thread (including a cancellation callback), and when the process already exited.
    /// Never throws for a process that will not die: if it is still running after the wait, <see cref="HasExited"/> stays false.
    /// </summary>
    public void Terminate()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        ProcessTree.TryTerminate(_process);
        _exited.Task.Wait(_terminateTimeout);
        _diagnostics.LogTermination(ProcessId, HasExited);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            ProcessTree.TryTerminate(_process);
            _exited.Task.Wait(_terminateTimeout);
            _diagnostics.LogTermination(ProcessId, HasExited);
        }
        catch
        {
            // disposal must not throw; the finally still releases everything
        }
        finally
        {
            try
            {
                StandardInput.Dispose();
            }
            catch (Exception)
            {
            }
            finally
            {
                _process.Dispose();
            }
        }
    }

    internal static CliProcessSession Create(CliCommand command)
    {
        ProcessLauncher.ValidateTimeout(command.TerminateTimeout, nameof(CliCommand.TerminateTimeout));
        ProcessTree.EnsureSupported();

        var diagnostics = new CommandDiagnostics(command);
        diagnostics.LogCommand("Session");
        var process = new Process { StartInfo = ProcessLauncher.CreateStartInfo(command.ToLaunchSpec(), redirectInput: true), EnableRaisingEvents = true };

        try
        {
            process.Start();
        }
        catch
        {
            process.Dispose();
            throw;
        }

        CliProcessSession? session = null;

        try
        {
            session = new CliProcessSession(process, command, diagnostics);
            process.Exited += (sender, e) => session._exited.TrySetResult(true);

            // Exited may have fired before the handler was attached
            if (process.HasExited)
            {
                session._exited.TrySetResult(true);
            }

            diagnostics.LogStarted(session.ProcessId);
            return session;
        }
        catch
        {
            if (session != null)
            {
                session.Dispose();
            }
            else
            {
                try
                {
                    ProcessTree.TryTerminate(process);
                    process.WaitForExit((int)(command.TerminateTimeout ?? DefaultTerminateTimeout).TotalMilliseconds);
                }
                finally
                {
                    process.Dispose();
                }
            }

            throw;
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(CliProcessSession));
        }
    }
}
