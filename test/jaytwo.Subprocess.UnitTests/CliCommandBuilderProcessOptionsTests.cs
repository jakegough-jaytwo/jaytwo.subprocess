using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.Subprocess.Streaming;
using Xunit;

namespace jaytwo.Subprocess.UnitTests;

public class CliCommandBuilderProcessOptionsTests : IDisposable
{
    private readonly TestWorkspace _workspace = new TestWorkspace();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void GetProcessOptions_arguments_match_GetCommand()
    {
        // arrange
        var builder = new CliCommandBuilder("node", "script.js", "with space", "trailing\\");

        // act
        var options = builder.GetProcessOptions();

        // assert
        Assert.Equal(builder.GetCommand().Arguments, options.Arguments);
    }

    [Fact]
    public async Task GetProcessOptions_passes_argument_edge_cases_through_unchanged()
    {
        // arrange
        var arguments = new[]
        {
            "plain",
            string.Empty,
            " ",
            "with space",
            "with \"quotes\"",
            "single 'quotes'",
            "back\\slash",
            "trailing\\",
            "trailing\\\\",
            "quote\\\"after-backslash",
            "\\\\\"double-backslash-quote",
            "日本語 ☃ café",
            "--flag=value with space",
            "{braces} %PERCENT% $DOLLAR",
        };

        var options = new CliCommandBuilder("node", _workspace.PathOf("argv.js"))
            .WithArguments(arguments)
            .GetProcessOptions();

        // act
        using var session = CliProcessSession.Start(options);
        var output = await session.StandardOutput.ReadToEndAsync();

        // assert
        var expected = arguments.Length + "\n" + string.Join("\n", Array.ConvertAll(arguments, x => TestWorkspace.ToHex(Encoding.UTF8.GetBytes(x))));
        Assert.Equal(expected, output);
    }

    [Fact]
    public void GetProcessOptions_passes_argument_edge_cases_through_unchanged_sync()
    {
        // arrange
        var arguments = new[] { "plain", string.Empty, "with \"quotes\"", "trailing\\", "日本語" };
        var options = new CliCommandBuilder("node", _workspace.PathOf("argv.js"))
            .WithArguments(arguments)
            .GetProcessOptions();

        // act
        using var session = CliProcessSession.Start(options);
        var output = session.StandardOutput.ReadToEnd();

        // assert
        var expected = arguments.Length + "\n" + string.Join("\n", Array.ConvertAll(arguments, x => TestWorkspace.ToHex(Encoding.UTF8.GetBytes(x))));
        Assert.Equal(expected, output);
    }

    [Fact]
    public void GetProcessOptions_copies_launch_settings()
    {
        // arrange
        var latin1 = Encoding.GetEncoding("latin1");
        var utf16 = Encoding.Unicode;
        var builder = new CliCommandBuilder("tool")
            .WithArgument("a b")
            .WithWorkingDirectory(_workspace.Directory)
            .WithEnvironmentVariable("SAMPLE_ENV", "yes")
            .WithSecret("sekret")
            .WithStandardOutputEncoding(latin1)
            .WithStandardErrorEncoding(Encoding.ASCII);
        builder.StandardInputEncoding = utf16;

        // act
        var options = builder.GetProcessOptions();

        // assert
        Assert.Equal("tool", options.FileName);
        Assert.Equal(builder.GetCommand().Arguments, options.Arguments);
        Assert.Equal(_workspace.Directory, options.WorkingDirectory);
        Assert.Equal("yes", options.Environment["SAMPLE_ENV"]);
        Assert.Equal(new[] { "sekret" }, options.Secrets);
        Assert.Same(utf16, options.StandardInputEncoding);
        Assert.Same(latin1, options.StandardOutputEncoding);
        Assert.Same(Encoding.ASCII, options.StandardErrorEncoding);
    }

    [Fact]
    public void GetProcessOptions_preserves_process_working_directory_independently_of_worker_output_directory_argument()
    {
        // arrange
        string outputDirectory = _workspace.Directory;
        string processDirectory = Path.GetTempPath();
        var builder = new CliCommandBuilder("node", "worker.js", outputDirectory)
            .WithWorkingDirectory(processDirectory);

        // act
        var options = builder.GetProcessOptions();

        // assert
        Assert.Equal(processDirectory, options.WorkingDirectory);
        Assert.Contains(outputDirectory, options.Arguments);
    }

    [Fact]
    public void GetProcessOptions_text_matches_builder_text_with_secrets_masked()
    {
        // arrange
        var builder = new CliCommandBuilder("tool", "token-value").WithSecret("token-value");

        // act
        var options = builder.GetProcessOptions();

        // assert
        Assert.Equal(builder.ToString(), options.ToString());
        Assert.DoesNotContain("token-value", options.ToString());
    }

    [Fact]
    public void GetProcessOptions_leaves_terminate_timeout_unset()
    {
        // arrange
        var builder = new CliCommandBuilder("node", "worker.js");

        // act
        var options = builder.GetProcessOptions();

        // assert
        Assert.Null(options.TerminateTimeout);
    }

    [Fact]
    public void GetProcessOptions_copies_terminate_timeout()
    {
        // arrange
        var builder = new CliCommandBuilder("node", "worker.js")
            .WithTerminateTimeout(TimeSpan.FromSeconds(10));

        // act
        var options = builder.GetProcessOptions();

        // assert
        Assert.Equal(TimeSpan.FromSeconds(10), options.TerminateTimeout);
    }

    [Fact]
    public void GetCommand_rejects_terminate_timeout()
    {
        // arrange
        var builder = new CliCommandBuilder("tool").WithTerminateTimeout(TimeSpan.FromSeconds(10));

        // act
        var exception = Assert.Throws<InvalidOperationException>(() => builder.GetCommand());

        // assert
        Assert.Contains(nameof(CliCommandBuilder.TerminateTimeout), exception.Message);
        Assert.Contains("GetProcessOptions", exception.Message);
    }

    [Fact]
    public void GetCommand_succeeds_after_terminate_timeout_is_cleared()
    {
        // arrange
        var builder = new CliCommandBuilder("tool")
            .WithTerminateTimeout(TimeSpan.FromSeconds(10))
            .WithTerminateTimeout(null);

        // act
        var command = builder.GetCommand();

        // assert
        Assert.Equal("tool", command.FileName);
    }

    [Fact]
    public void GetProcessOptions_snapshots_environment_and_secrets()
    {
        // arrange
        var builder = new CliCommandBuilder("tool")
            .WithEnvironmentVariable("A", "1")
            .WithSecret("one");

        // act
        var options = builder.GetProcessOptions();
        builder.WithEnvironmentVariable("A", "changed");
        builder.WithSecret("two");
        options.Environment["A"] = "options-changed";
        options.Secrets.Add("three");

        // assert
        Assert.Equal("changed", builder.EnvironmentVariables["A"]);
        Assert.Equal(new[] { "one", "two" }, builder.Secrets);
        Assert.Equal("options-changed", options.Environment["A"]);
        Assert.Equal(new[] { "one", "three" }, options.Secrets);
    }

    [Fact]
    public void GetProcessOptions_returns_a_new_instance_each_call()
    {
        // arrange
        var builder = new CliCommandBuilder("tool", "a");

        // act
        var first = builder.GetProcessOptions();
        var second = builder.GetProcessOptions();

        // assert
        Assert.NotSame(first, second);
        Assert.NotSame(first.Secrets, second.Secrets);
        Assert.NotSame(first.Environment, second.Environment);
        Assert.Equal(first.Arguments, second.Arguments);
    }

    [Fact]
    public void GetProcessOptions_applies_runtime_conditioned_settings()
    {
        // arrange
        var builder = new CliCommandBuilder("tool")
            .WithRuntimeCondition(_ => true, b => b.WithEnvironmentVariable("COND", "yes"));

        // act
        var options = builder.GetProcessOptions();

        // assert
        Assert.Equal("yes", options.Environment["COND"]);
    }

    [Fact]
    public void GetProcessOptions_succeeds_for_a_default_builder()
    {
        // arrange
        var parameterless = new CliCommandBuilder();
        var withArgs = new CliCommandBuilder("node", "a", "b");

        // act
        var fromParameterless = parameterless.GetProcessOptions();
        var fromArgs = withArgs.GetProcessOptions();

        // assert
        Assert.Equal(string.Empty, fromParameterless.FileName);
        Assert.Equal(string.Empty, fromParameterless.Arguments);
        Assert.Equal("node", fromArgs.FileName);
        Assert.Equal(withArgs.GetCommand().Arguments, fromArgs.Arguments);
        Assert.Null(fromArgs.TerminateTimeout);
    }

    [Fact]
    public void GetProcessOptions_rejects_standard_input()
    {
        // arrange
        var builder = new CliCommandBuilder("tool").WithStandardInput("payload");

        // act
        var exception = Assert.Throws<InvalidOperationException>(() => builder.GetProcessOptions());

        // assert
        Assert.Contains(nameof(CliCommandBuilder.StandardInput), exception.Message);
        Assert.DoesNotContain("payload", exception.Message);
    }

    [Fact]
    public void GetProcessOptions_rejects_empty_standard_input()
    {
        // arrange
        var builder = new CliCommandBuilder("tool").WithStandardInput(string.Empty);

        // act
        var exception = Assert.Throws<InvalidOperationException>(() => builder.GetProcessOptions());

        // assert
        Assert.Contains(nameof(CliCommandBuilder.StandardInput), exception.Message);
    }

    [Fact]
    public void GetProcessOptions_rejects_standard_input_stream()
    {
        // arrange
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var builder = new CliCommandBuilder("tool").WithStandardInput(stream);

        // act
        var exception = Assert.Throws<InvalidOperationException>(() => builder.GetProcessOptions());

        // assert
        Assert.Contains(nameof(CliCommandBuilder.StandardInputStream), exception.Message);
        Assert.True(stream.CanRead);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void GetProcessOptions_rejects_timeout()
    {
        // arrange
        var builder = new CliCommandBuilder("tool").WithTimeout(TimeSpan.Zero);

        // act
        var exception = Assert.Throws<InvalidOperationException>(() => builder.GetProcessOptions());

        // assert
        Assert.Contains(nameof(CliCommandBuilder.Timeout), exception.Message);
    }

    [Fact]
    public void GetProcessOptions_rejects_expected_exit_codes()
    {
        // arrange
        var builder = new CliCommandBuilder("tool").WithExpectedExitCode(0);

        // act
        var exception = Assert.Throws<InvalidOperationException>(() => builder.GetProcessOptions());

        // assert
        Assert.Contains(nameof(CliCommandBuilder.ExpectedExitCodes), exception.Message);
    }

    [Fact]
    public void GetProcessOptions_rejects_output_drain_timeout()
    {
        // arrange
        var builder = new CliCommandBuilder("tool").WithOutputDrainTimeout(Timeout.InfiniteTimeSpan);

        // act
        var exception = Assert.Throws<InvalidOperationException>(() => builder.GetProcessOptions());

        // assert
        Assert.Contains(nameof(CliCommandBuilder.OutputDrainTimeout), exception.Message);
    }

    [Fact]
    public void GetProcessOptions_rejects_and_names_every_unsupported_setting()
    {
        // arrange
        var builder = new CliCommandBuilder("tool")
            .WithStandardInput("secret-stdin")
            .WithTimeout(TimeSpan.FromSeconds(1))
            .WithExpectedExitCode(1)
            .WithOutputDrainTimeout(TimeSpan.FromSeconds(2));
        builder.StandardInputStream = new MemoryStream();

        // act
        var exception = Assert.Throws<InvalidOperationException>(() => builder.GetProcessOptions());

        // assert
        Assert.Contains(nameof(CliCommandBuilder.StandardInput), exception.Message);
        Assert.Contains(nameof(CliCommandBuilder.StandardInputStream), exception.Message);
        Assert.Contains(nameof(CliCommandBuilder.Timeout), exception.Message);
        Assert.Contains(nameof(CliCommandBuilder.ExpectedExitCodes), exception.Message);
        Assert.Contains(nameof(CliCommandBuilder.OutputDrainTimeout), exception.Message);
        Assert.DoesNotContain("secret-stdin", exception.Message);
        Assert.True(
            exception.Message.IndexOf(nameof(CliCommandBuilder.StandardInput), StringComparison.Ordinal) <
            exception.Message.IndexOf(nameof(CliCommandBuilder.StandardInputStream), StringComparison.Ordinal));
    }

    [Fact]
    public void GetProcessOptions_does_not_echo_standard_input_text()
    {
        // arrange
        var builder = new CliCommandBuilder("tool").WithStandardInput("super-secret-token");

        // act
        var exception = Assert.Throws<InvalidOperationException>(() => builder.GetProcessOptions());

        // assert
        Assert.DoesNotContain("super-secret-token", exception.Message);
    }

    [Fact]
    public void GetProcessOptions_succeeds_after_settings_are_cleared()
    {
        // arrange
        var builder = new CliCommandBuilder("tool")
            .WithStandardInput("x")
            .WithTimeout(TimeSpan.FromSeconds(1))
            .WithExpectedExitCode(0)
            .WithOutputDrainTimeout(TimeSpan.FromSeconds(1));
        builder.StandardInput = null;
        builder.StandardInputStream = null;
        builder.WithTimeout(null);
        builder.WithExpectedExitCodes();
        builder.WithOutputDrainTimeout(null);

        // act
        var options = builder.GetProcessOptions();

        // assert
        Assert.Equal("tool", options.FileName);
    }

    [Fact]
    public void ToString_works_when_terminate_timeout_is_set()
    {
        // arrange
        var builder = new CliCommandBuilder("tool", "arg")
            .WithTerminateTimeout(TimeSpan.FromSeconds(10));

        // act
        var text = builder.ToString();

        // assert
        Assert.Equal(builder.GetProcessOptions().ToString(), text);
    }
}
