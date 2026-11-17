using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using jaytwo.Subprocess.Streaming;
using Xunit;

namespace jaytwo.Subprocess.UnitTests.Guides;

// The samples in docs/guides/pass-arguments.md.
public class PassArgumentsGuideTests : IDisposable
{
    private readonly TestWorkspace _workspace = new TestWorkspace();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task pass_arguments()
    {
        // arrange
        File.WriteAllText(_workspace.PathOf("print.js"), "console.log(process.argv.slice(2).join('|'));");
        string scriptPath = _workspace.PathOf("print.js");
        string message = "hello world";
        string workingDirectory = _workspace.Directory;

        // act
        // sample: pass-arguments-basic
        var command = new CliCommandBuilder("node", scriptPath)
            .WithArgument("--message")
            .WithArgument(message)
            .WithWorkingDirectory(workingDirectory)
            .WithEnvironmentVariable("MY_TOOL_PROMPT", "0")
            .GetCommand();

        CliCommandResult result = await command.ExecuteAsync();
        // end sample

        // assert
        Assert.True(result.Success);
        Assert.Contains("--message", result.StandardOutput);
        Assert.Contains(message, result.StandardOutput);
    }

    [Fact]
    public async Task pass_formatted_arguments()
    {
        // arrange
        string message = "hello world";

        // act
        // sample: pass-arguments-format
        var command = new CliCommandBuilder("node", "-e")
            .WithArgument("console.log('{0}')", message)
            .GetCommand();
        // end sample

        // assert
        CliCommandResult result = await command.ExecuteAsync();
        Assert.Equal(message, result.StandardOutput?.Trim());
    }

    [Fact]
    public async Task pass_working_directory_and_environment()
    {
        // arrange
        File.WriteAllText(_workspace.PathOf("env.js"), "console.log(process.env.DOTNET_CLI_UI_LANGUAGE + '|' + process.env.EXTRA);");
        string scriptPath = _workspace.PathOf("env.js");
        string projectDirectory = _workspace.Directory;
        var extraVars = new Dictionary<string, string> { ["EXTRA"] = "yes" };

        // act
        // sample: pass-arguments-env
        var command = new CliCommandBuilder("node", scriptPath)
            .WithWorkingDirectory(projectDirectory)
            .WithEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE", "en")
            .WithEnvironmentVariables(extraVars)
            .GetCommand();
        // end sample

        // assert
        CliCommandResult result = await command.ExecuteAsync();
        Assert.Equal("en|yes", result.StandardOutput?.Trim());
    }

    [Fact]
    public void reuse_quoted_arguments_for_a_session()
    {
        // arrange
        string workerScript = _workspace.PathOf("worker.js");
        string outputDirectory = _workspace.Directory;
        string mode = "echo";

        // act
        // sample: pass-arguments-session
        var options = new CliCommandBuilder("node", workerScript, outputDirectory, "--mode", mode)
            .GetProcessOptions();

        using var session = CliProcessSession.Start(options);
        // end sample

        // assert
        Assert.False(session.HasExited);
        Assert.Contains("--mode", options.Arguments);
    }
}
