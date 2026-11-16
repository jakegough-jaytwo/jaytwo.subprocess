using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace jaytwo.Subprocess.UnitTests.Guides;

// The samples in docs/guides/mask-secrets.md.
public class MaskSecretsGuideTests
{
    [Fact]
    public async Task mask_secrets_in_diagnostics()
    {
        // arrange
        var configuration = new Dictionary<string, string> { ["ApiKey"] = "super-secret-key" };

        // act
        // sample: mask-secrets
        string apiKey = configuration["ApiKey"];

        var command = new CliCommandBuilder("node", "-e")
            .WithArgument("console.log(process.argv[1])")
            .WithArgument("Authorization: Bearer {0}", apiKey)
            .WithSecret(apiKey)
            .GetCommand();

        Console.WriteLine(command);   // Authorization header shows **** instead of the key

        CliCommandResult result = await command.ExecuteAsync();
        // result.StandardOutput is unmasked; do not log it raw if it can echo the secret
        // end sample

        // assert
        Assert.DoesNotContain(apiKey, command.ToString());
        Assert.Contains("****", command.ToString());
        Assert.Contains(apiKey, result.StandardOutput);
    }
}
