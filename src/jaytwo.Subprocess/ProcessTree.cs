using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace jaytwo.Subprocess;

/// <summary>
/// Whole-tree termination, made explicit per framework:
/// net6.0+ calls Process.Kill(entireProcessTree: true).
/// netstandard2.0 calls that same method by reflection when the host runtime has it (.NET Core 3.0+, .NET 5+),
/// else uses taskkill /T /F on Windows (.NET Framework), else is unsupported and fails fast (see EnsureSupported).
/// It never falls back to killing only the direct process.
/// </summary>
internal static class ProcessTree
{
#if NETSTANDARD2_0
    private static readonly MethodInfo? KillEntireTreeMethod = typeof(Process).GetMethod(nameof(Process.Kill), new[] { typeof(bool) });

    private static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;
#endif

    public static void EnsureSupported()
    {
#if NETSTANDARD2_0
        if (KillEntireTreeMethod == null && !IsWindows)
        {
            throw new PlatformNotSupportedException("Terminating a process tree requires .NET Core 3.0+ / .NET 5+ or Windows; refusing to run a command that could only be killed partially.");
        }
#endif
    }

    /// <summary>
    /// Terminates the process and its descendants. Returns false (doing nothing) if the process had already exited.
    /// Throws if the process is still running after a failed attempt.
    /// </summary>
    public static bool TryTerminate(Process process)
    {
        if (HasExited(process))
        {
            return false;
        }

        try
        {
            Kill(process);
            return true;
        }
        catch when (HasExited(process))
        {
            // lost a race with a natural exit; partial-failure errors for already-exited descendants are expected
            return false;
        }
    }

    internal static void Kill(Process process)
    {
#if NETSTANDARD2_0
        if (KillEntireTreeMethod != null)
        {
            try
            {
                KillEntireTreeMethod.Invoke(process, new object[] { true });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        }
        else
        {
            TaskKill(process.Id);
        }
#else
        process.Kill(entireProcessTree: true);
#endif
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

#if NETSTANDARD2_0
    private static void TaskKill(int processId)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), $"/PID {processId} /T /F")
        {
            UseShellExecute = false,
            CreateNoWindow = true, // output is deliberately not redirected: nothing would drain it
        };

        using (var taskkill = Process.Start(startInfo)!)
        {
            if (!taskkill.WaitForExit(15000))
            {
                try
                {
                    taskkill.Kill();
                }
                catch (InvalidOperationException)
                {
                }

                throw new InvalidOperationException($"taskkill did not finish terminating process {processId}");
            }

            if (taskkill.ExitCode != 0)
            {
                throw new InvalidOperationException($"taskkill failed to terminate process {processId} (exit code {taskkill.ExitCode})");
            }
        }
    }
#endif
}
