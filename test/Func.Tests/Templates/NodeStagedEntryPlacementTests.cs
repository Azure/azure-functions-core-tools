// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Templates.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("js", "JavaScript", ".js")]
    [InlineData("ts", "TypeScript", ".ts")]
    public async Task NodeEntryPlacement_ExistingFunctionsNestedBase_IsNotDeduplicatedAndStagedPathRemainsLiteral(
        string alias, string language, string extension)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = Path.Combine(_root, "entry-placement-" + alias);
        var entry = new NativeCliEntryPoint();
        (await entry.ExecuteAsync(["init", project, "-s", "node", "-l", alias, "--no-bundles", "--skip-npm-install"],
            _interaction, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ => Task.FromResult<string?>(null)))
            .Should().Be(0, _interaction.AllOutput);
        string nested = Path.Combine(project, "src", "functions");
        TestInteractionService output = new();

        int exit = await entry.ExecuteAsync(["new", nested, "-t", "HttpTrigger-" + language, "--non-interactive", "--output=json"],
            output, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ => Task.FromResult<string?>(null));

        exit.Should().Be(0, output.AllOutput);
        string relative = Path.Combine("src", "functions", "httpTrigger" + extension);
        File.ReadAllText(Path.Combine(nested, relative)).Should().Contain("app.http('httpTrigger'");
        File.Exists(Path.Combine(nested, "httpTrigger" + extension)).Should().BeFalse();
        using (var json = JsonDocument.Parse(output.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]))
        {
            json.RootElement.GetProperty("executionDirectory").GetString().Should().Be(nested);
            json.RootElement.GetProperty("outputDirectory").GetString().Should().Be(nested);
            json.RootElement.GetProperty("invocation").GetProperty("changes")[0].GetProperty("path").GetString()
                .Should().Be("src/functions/httpTrigger" + extension);
        }
        TestInteractionService explicitOutput = new();
        int explicitlyNamed = await entry.ExecuteAsync(
            ["new", project, "-t", "HttpTrigger-" + language, "--name", "ExplicitAuthored", "--authlevel", "function", "--non-interactive", "--output=json"],
            explicitOutput, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ => Task.FromResult<string?>(null));
        explicitlyNamed.Should().Be(0, explicitOutput.AllOutput);
        File.ReadAllText(Path.Combine(project, "src", "functions", "ExplicitAuthored" + extension))
            .Should().Contain("app.http('ExplicitAuthored'", "authLevel: 'function'");
        File.Exists(Path.Combine(project, "ExplicitAuthored" + extension)).Should().BeFalse();
        using var host = await CreateNodePlacementHostAsync();
        foreach (string literal in new[] { project, nested })
        {
            using var invocation = host.CreateInvocation();
            var availability = await invocation.Services.GetRequiredService<INativeTemplateAvailability>().CaptureAsync(CancellationToken.None);
            var command = new NewExecutionCommand(invocation.Services.GetRequiredService<INewExecutionRunner>(),
                invocation.Services.GetRequiredService<NewExecutionRenderer>(), availability);

            int created = await InvokeNodeBridgeAsync(command.ParseInvocation(
                ["HttpTrigger-" + language, "--path", literal, "--name", "LiteralStaged", "--non-interactive", "--output", "json"]));

            created.Should().Be(0, _interaction.AllOutput);
            File.ReadAllText(Path.Combine(literal, "LiteralStaged" + extension)).Should().Contain("app.http('LiteralStaged'");
        }
    }

    [Theory]
    [InlineData("src")]
    [InlineData("src/functions")]
    public async Task NodeEntryPlacement_UserPrefixFile_IsOneJsonRefusalWithoutLegacyFallback(string prefix)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        var entry = new NativeCliEntryPoint();
        string project = Path.Combine(_root, "entry-file-prefix");
        (await entry.ExecuteAsync(["init", project, "-s", "node", "-l", "js", "--no-bundles", "--skip-npm-install"],
            _interaction, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ => Task.FromResult<string?>(null))).Should().Be(0);
        string blocked = Path.Combine(project, prefix.Replace('/', Path.DirectorySeparatorChar));
        Directory.Delete(blocked, recursive: true);
        File.WriteAllText(blocked, "user-owned prefix");
        TestInteractionService output = new();

        int exit = await entry.ExecuteAsync(["new", project, "-t", "HttpTrigger-JavaScript", "--non-interactive", "--output=json"],
            output, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ => Task.FromResult<string?>(null));

        exit.Should().Be(1, output.AllOutput);
        File.ReadAllText(blocked).Should().Be("user-owned prefix");
        File.Exists(Path.Combine(project, "httpTrigger.js")).Should().BeFalse();
        using var json = JsonDocument.Parse(output.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
        json.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        output.AllOutput.Should().NotContain("INPUT:").And.NotContain("SELECT:").And.NotContain("SUCCESS:");
    }

    [SymbolicLinkFact]
    public async Task NodeEntryPlacement_LinkedDerivedPrefix_IsRefusedAcrossActualFullEntry()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        var entry = new NativeCliEntryPoint();
        string project = Path.Combine(_root, "entry-linked-prefix");
        (await entry.ExecuteAsync(["init", project, "-s", "node", "-l", "ts", "--no-bundles", "--skip-npm-install"],
            _interaction, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ => Task.FromResult<string?>(null))).Should().Be(0);
        string outside = Directory.CreateDirectory(Path.Combine(_root, "entry-outside")).FullName;
        string link = Path.Combine(project, "src");
        Directory.Delete(link, recursive: true);
        Directory.CreateSymbolicLink(link, outside);
        try
        {
            TestInteractionService output = new();

            int exit = await entry.ExecuteAsync(["new", project, "-t", "HttpTrigger-TypeScript", "--non-interactive", "--output=json"],
                output, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ => Task.FromResult<string?>(null));

            exit.Should().Be(1, output.AllOutput);
            Directory.GetFileSystemEntries(outside).Should().BeEmpty();
            File.Exists(Path.Combine(project, "httpTrigger.ts")).Should().BeFalse();
            using var json = JsonDocument.Parse(output.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
            json.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Theory]
    [InlineData("--InvocationOutputIntent=legacy")]
    [InlineData("--invocation-output-intent=literal")]
    [InlineData("--param:InvocationOutputIntent=literal")]
    public async Task NodeEntryPlacement_BoundIntentCannotBeSuppliedAsTemplateOption(string token)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        var entry = new NativeCliEntryPoint();
        string project = Path.Combine(_root, "entry-intent-input");
        (await entry.ExecuteAsync(["init", project, "-s", "node", "-l", "js", "--no-bundles", "--skip-npm-install"],
            _interaction, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ => Task.FromResult<string?>(null))).Should().Be(0);
        TestInteractionService output = new();

        int exit = await entry.ExecuteAsync(["new", project, "-t", "HttpTrigger-JavaScript", "--non-interactive", "--output=json", token],
            output, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ => Task.FromResult<string?>(null));

        exit.Should().Be(1, output.AllOutput);
        using var json = JsonDocument.Parse(output.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
        json.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        Directory.GetFiles(Path.Combine(project, "src", "functions"), "*", SearchOption.AllDirectories).Should().BeEmpty();
        File.Exists(Path.Combine(project, "httpTrigger.js")).Should().BeFalse();
    }
}
