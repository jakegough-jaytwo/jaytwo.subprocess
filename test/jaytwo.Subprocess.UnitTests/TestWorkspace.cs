using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Sdk;

namespace jaytwo.Subprocess.UnitTests;

/// <summary>
/// A temp directory holding small deterministic node child programs. Children signal readiness by atomically writing files
/// (never by sleeping) and record their PIDs in *.pid files; Dispose kills anything still alive and deletes the directory,
/// so probes clean up after themselves even when an assertion fails.
/// </summary>
internal sealed class TestWorkspace : IDisposable
{
    public const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private const string Prelude = """
        var fs = require('fs');
        var path = require('path');
        var cp = require('child_process');
        function w(fd, data) {
          var b = Buffer.isBuffer(data) ? data : Buffer.from(String(data), 'utf8');
          var o = 0;
          while (o < b.length) {
            try { o += fs.writeSync(fd, b, o, b.length - o); }
            catch (e) { if (e.code !== 'EAGAIN') throw e; }
          }
        }
        function writeAtomic(file, text) { var tmp = file + '.tmp'; fs.writeFileSync(tmp, String(text)); fs.renameSync(tmp, file); }
        function hold() { setInterval(function () {}, 1000); }
        // detached matters on Windows: libuv otherwise puts the child in a kill-on-close job, so it would die with its parent and prove nothing
        function spawnSleeper(dir) { return cp.spawn(process.execPath, [path.join(dir, 'sleeper.js'), dir, 'grandchild'], { stdio: 'inherit', detached: true }); }
        function whenFile(file, action) {
          var t = setInterval(function () { if (fs.existsSync(file)) { clearInterval(t); action(); } }, 20);
        }
        """;

    private static readonly Dictionary<string, string> Scripts = new Dictionary<string, string>
    {
        // emit.js <n>: interleaves n chars on stdout and n chars on stderr so both pipes fill
        ["emit.js"] = """
            var n = parseInt(process.argv[2], 10);
            var alphabet = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';
            function gen(offset, length) { var s = ''; for (var i = 0; i < length; i++) { s += alphabet[(offset + i) % alphabet.length]; } return s; }
            for (var o = 0; o < n; o += 8192) {
              var len = Math.min(8192, n - o);
              w(1, gen(o, len));
              w(2, gen(o + 7, len));
            }
            """,

        ["exact.js"] = """
            w(1, 'line1\n\nline3\r\n\r\nlast');
            w(2, 'e1\r\n\n\nend');
            """,

        // encoding.js <stdoutEncoding> <stderrEncoding>
        ["encoding.js"] = """
            var latin = 'café ñ ü';
            var uni = 'café 日本語 ☃ 😀';
            function text(enc) { return enc === 'latin1' ? latin : uni; }
            w(1, Buffer.from(text(process.argv[2]), process.argv[2]));
            w(2, Buffer.from(text(process.argv[3]), process.argv[3]));
            """,

        // echoes stdin to both stdout and stderr as it arrives, then marks EOF on stdout
        ["echo.js"] = """
            process.stdin.on('data', function (c) { w(1, c); w(2, c); });
            process.stdin.on('end', function () { w(1, '<EOF>'); });
            """,

        // prints the bytes it read from stdin as hex once stdin reaches EOF
        ["hex.js"] = """
            var chunks = [];
            process.stdin.on('data', function (c) { chunks.push(c); });
            process.stdin.on('end', function () { w(1, Buffer.concat(chunks).toString('hex')); });
            """,

        // exit.js <code>: does not read stdin
        ["exit.js"] = """
            w(1, 'out-text');
            w(2, 'err-text');
            process.exit(parseInt(process.argv[2], 10));
            """,

        ["argv.js"] = """
            var args = process.argv.slice(2);
            w(1, String(args.length) + '\n' + args.map(function (a) { return Buffer.from(a, 'utf8').toString('hex'); }).join('\n'));
            """,

        ["echoarg.js"] = """
            w(1, 'out:' + process.argv[2]);
            w(2, 'err:' + process.argv[2]);
            """,

        // pid.js <dir> [hold]: prints its pid; with 'hold' it stays alive without ever reading stdin
        ["pid.js"] = """
            var dir = process.argv[2];
            writeAtomic(path.join(dir, 'pid.pid'), process.pid);
            w(1, String(process.pid));
            if (process.argv[3] === 'hold') { hold(); }
            """,

        // sleeper.js <dir> <name>
        ["sleeper.js"] = """
            writeAtomic(path.join(process.argv[2], process.argv[3] + '.pid'), process.pid);
            hold();
            """,

        // tree.js <dir>: parent plus a descendant that inherits (holds) the redirected pipes; ready once both are running
        ["tree.js"] = """
            var dir = process.argv[2];
            writeAtomic(path.join(dir, 'parent.pid'), process.pid);
            spawnSleeper(dir);
            whenFile(path.join(dir, 'grandchild.pid'), function () {
              w(1, 'parent-out');
              w(2, 'parent-err');
              writeAtomic(path.join(dir, 'ready'), '1');
              hold();
            });
            """,

        // escape.js <dir>: the descendant is orphaned (its parent exits) so tree termination cannot reach it, yet it still holds the pipes
        ["escape.js"] = """
            var dir = process.argv[2];
            writeAtomic(path.join(dir, 'parent.pid'), process.pid);
            var middle = cp.spawn(process.execPath, [path.join(dir, 'middle.js'), dir], { stdio: 'inherit' });
            middle.on('exit', function () {
              w(1, 'parent-out');
              w(2, 'parent-err');
              writeAtomic(path.join(dir, 'ready'), '1');
              hold();
            });
            """,

        ["middle.js"] = """
            var dir = process.argv[2];
            spawnSleeper(dir);
            whenFile(path.join(dir, 'grandchild.pid'), function () { process.exit(0); });
            """,

        // holdpipes.js <dir>: the parent exits normally while a descendant keeps the pipes open
        ["holdpipes.js"] = """
            var dir = process.argv[2];
            writeAtomic(path.join(dir, 'parent.pid'), process.pid);
            spawnSleeper(dir);
            whenFile(path.join(dir, 'grandchild.pid'), function () {
              w(1, 'parent-out');
              w(2, 'parent-err');
              process.exit(0);
            });
            """,

        // worker.js <dir>: line-oriented request/response worker
        ["worker.js"] = """
            var dir = process.argv[2];
            writeAtomic(path.join(dir, 'worker.pid'), process.pid);
            var rl = require('readline').createInterface({ input: process.stdin });
            rl.on('line', function (line) {
              w(2, 'saw:' + line + '\n');
              if (line === 'crash') { process.exit(3); }
              else if (line === 'hang') { }
              else if (line === 'spawn') {
                cp.spawn(process.execPath, [path.join(dir, 'sleeper.js'), dir, 'grandchild'], { stdio: 'ignore', detached: true });
                whenFile(path.join(dir, 'grandchild.pid'), function () { w(1, 'spawned\n'); });
              }
              else { w(1, 'echo:' + line + '\n'); }
            });
            rl.on('close', function () { process.exit(0); });
            """,
    };

    private static readonly string[] ProbeProcessNames = { "node", "sleep", "sh", "dash", "bash" };

    private readonly List<int> _trackedProcessIds = new List<int>();
    private readonly DateTime _createdUtc = DateTime.UtcNow;

    public TestWorkspace()
    {
        Directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jaytwo.Subprocess.Tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);

        foreach (var script in Scripts)
        {
            File.WriteAllText(System.IO.Path.Combine(Directory, script.Key), Prelude + Environment.NewLine + script.Value);
        }
    }

    public string Directory { get; }

    public static string Generate(int offset, int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[(offset + i) % Alphabet.Length];
        }

        return new string(chars);
    }

    public static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();

    public static async Task<TException> CaptureAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException ex)
        {
            return ex;
        }

        throw new XunitException($"Expected {typeof(TException).Name} but nothing was thrown.");
    }

    public static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }

        throw new XunitException($"Expected {typeof(TException).Name} but nothing was thrown.");
    }

    public static bool IsAlive(int processId)
    {
        try
        {
            using (var process = Process.GetProcessById(processId))
            {
                return !process.HasExited && IsProbeProcess(process);
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            Thread.Sleep(20);
        }

        return true;
    }

    public static bool WaitUntilDead(int processId) => WaitUntil(() => !IsAlive(processId), TimeSpan.FromSeconds(20));

    public string PathOf(string name) => System.IO.Path.Combine(Directory, name);

    public CliCommandBuilder Node(string script, params string[] args)
    {
        var builder = new CliCommandBuilder("node")
            .WithArgument(PathOf(script))
            .WithTimeout(TimeSpan.FromSeconds(30));

        foreach (var arg in args)
        {
            builder.WithArgument(arg);
        }

        return builder;
    }

    public CliCommandBuilder NodeWithDirectory(string script, params string[] args)
    {
        var allArgs = new string[args.Length + 1];
        allArgs[0] = Directory;
        Array.Copy(args, 0, allArgs, 1, args.Length);
        return Node(script, allArgs);
    }

    public CliCommand NodeSessionCommand(string script, params string[] args)
    {
        // Node() installs a one-shot Timeout; clear it before Start.
        return NodeWithDirectory(script, args)
            .WithTimeout(null)
            .WithTerminateTimeout(TimeSpan.FromSeconds(10))
            .GetCommand();
    }

    public bool WaitForFile(string name) => WaitUntil(() => File.Exists(PathOf(name)), TimeSpan.FromSeconds(60));

    public async Task<bool> WaitForFileAsync(string name)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!File.Exists(PathOf(name)))
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(60))
            {
                return false;
            }

            await Task.Delay(20);
        }

        return true;
    }

    public int ReadProcessId(string pidFile) => int.Parse(File.ReadAllText(PathOf(pidFile)).Trim());

    public void Track(int processId)
    {
        lock (_trackedProcessIds)
        {
            _trackedProcessIds.Add(processId);
        }
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in System.IO.Directory.GetFiles(Directory, "*.pid"))
            {
                try
                {
                    Kill(int.Parse(File.ReadAllText(file).Trim()));
                }
                catch (Exception)
                {
                }
            }

            lock (_trackedProcessIds)
            {
                _trackedProcessIds.ForEach(Kill);
            }
        }
        finally
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    System.IO.Directory.Delete(Directory, recursive: true);
                    break;
                }
                catch (Exception)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }

    // pids are recycled, so only ever touch the kinds of process the probes launch (and, when killing, only ones started after this workspace was created)
    private static bool IsProbeProcess(Process process) => Array.IndexOf(ProbeProcessNames, process.ProcessName.ToLowerInvariant()) >= 0;

    private void Kill(int processId)
    {
        try
        {
            using (var process = Process.GetProcessById(processId))
            {
                if (!process.HasExited && IsProbeProcess(process) && process.StartTime.ToUniversalTime() >= _createdUtc.AddSeconds(-1))
                {
                    ProcessTree.Kill(process);
                }
            }
        }
        catch (Exception)
        {
        }
    }
}
