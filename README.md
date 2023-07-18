# jaytwo.Subprocess

<p align="center">
  <a href="https://jenkins.jaytwo.com/job/jaytwo.Subprocess/job/master/" alt="Build Status (master)">
    <img src="https://jenkins.jaytwo.com/buildStatus/icon?job=jaytwo.Subprocess%2Fmaster&subject=build%20(master)" /></a>
  <a href="https://jenkins.jaytwo.com/job/jaytwo.Subprocess/job/develop/" alt="Build Status (develop)">
    <img src="https://jenkins.jaytwo.com/buildStatus/icon?job=jaytwo.Subprocess%2Fdevelop&subject=build%20(develop)" /></a>
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/jaytwo.Subprocess/" alt="NuGet Package jaytwo.Subprocess">
    <img src="https://img.shields.io/nuget/v/jaytwo.Subprocess.svg?logo=nuget&label=jaytwo.Subprocess" /></a>
  <a href="https://www.nuget.org/packages/jaytwo.Subprocess/" alt="NuGet Package jaytwo.Subprocess (beta)">
    <img src="https://img.shields.io/nuget/vpre/jaytwo.Subprocess.svg?logo=nuget&label=jaytwo.Subprocess" /></a>
</p>

A simplified way to invoke external processes in .NET

## Installation

Add the NuGet package

```
PM> Install-Package jaytwo.subprocess
```

## Usage

It's got a fluent command builder that makes things pretty easy, and its usage should be pretty easy to figure out.

```csharp
using jaytwo.Subprocess;

// ...

var message = "hello world";

var command = new CliCommandBuilder("node", "-e")
    .WithArgument("console.log('{0}')", message)
    .GetCommand();

var result = await command.ExecuteAsync();

Console.WriteLine("Command: {0}", result.Command);
Console.WriteLine("ExitCode: {0}", result.ExitCode);
Console.WriteLine("StandardOutput: {0}", result.StandardOutput);
Console.WriteLine("StandardError: {0}", result.StandardError);
```

## Advanced Usage

There are a few features that you'll probably never use or need... but I needed them, so maybe you will too.

### Runtime Conditions

Sometimes invoking commands just does't work the same on different platforms.  For example, if my app is developed on Windows but deployed
to a Linux container, I would like it to work reliably in both places.

The [Yeoman NPM Package](https://www.npmjs.com/package/yo) can be invoked on linux with with `yo` directly, but no such luck on Windows.  
(Even though it works from a Bash or CMD shell on Windows... if you run `which yo` in Bash or `where yo` in CMD, and you'll see that 
`yo` just points to shortcuts/scripts that the shell knows what  to do with.)  To work around this, we can invoke `yo` from the command 
interpreter itself on windows with `cmd /c yo`.

```csharp
using jaytwo.Subprocess;

// ...

var message = "hello world";

var command = new CliCommandBuilder()
    .WithRuntimeCondition(x => x.Platform != Runtime.OSPlatform.Windows, builder =>
    {
        // specific for non-Windows platforms
        builder.WithFileName("yo");
    })
    .WithRuntimeCondition(x => x.Platform == Runtime.OSPlatform.Windows, builder =>
    {
        // specific for Windows platform
        builder.WithFileName("cmd").WithArguments("/C", "yo");
    })
    .GetCommand();
```

---

Made with &hearts; by Jake
