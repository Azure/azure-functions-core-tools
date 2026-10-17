// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Commands.Setup;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("JavaScript", "js", ".js")]
    [InlineData("TypeScript", "ts", ".ts")]
    public async Task StagedEntry_FullPublicGrammar_InitAndRootOrNestedNewPreserveLegacyNodePlacement(
        string language, string alias, string extension)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = Path.Combine(_root, "public-" + alias);
        var entry = new NativeCliEntryPoint();
        int initialized = await entry.ExecuteAsync(["init", project, "-s", "node", "-l", alias, "--no-bundles", "--skip-npm-install"],
            _interaction, _paths, new(Home), CancellationToken.None, configure: TrackEntryContexts, versionCheck: _ => Task.FromResult<string?>(null));
        initialized.Should().Be(0, _interaction.AllOutput);
        File.Exists(Path.Combine(project, ".func", "config.json")).Should().BeTrue();

        foreach (string outputBase in new[] { project, Path.Combine(project, "nested") })
        {
            TestInteractionService output = new();
            int versionCalls = 0;
            int exit = await entry.ExecuteAsync(["--verbose=true", "new", "--authlevel=anonymous", outputBase,
                "-t", "HttpTrigger-" + language, "--non-interactive", "--output", "json"],
                output, _paths, new(Home), CancellationToken.None, configure: TrackEntryContexts, versionCheck: _ =>
                {
                    versionCalls++;
                    return Task.FromResult<string?>("9999.0.0");
                });

            exit.Should().Be(0, output.AllOutput);
            versionCalls.Should().Be(0);
            File.Exists(Path.Combine(outputBase, "src", "functions", "httpTrigger" + extension)).Should().BeTrue();
            File.Exists(Path.Combine(outputBase, "httpTrigger" + extension)).Should().BeFalse();
            string line = output.Lines.Single(value => value.StartsWith("JSON: ", StringComparison.Ordinal));
            using var json = JsonDocument.Parse(line["JSON: ".Length..]);
            json.RootElement.GetProperty("executionDirectory").GetString().Should().Be(outputBase);
            json.RootElement.GetProperty("outputDirectory").GetString().Should().Be(outputBase);
            json.RootElement.GetProperty("invocation").GetProperty("changes")[0].GetProperty("path").GetString()
                .Should().Be("src/functions/httpTrigger" + extension);
            output.AllOutput.Should().NotContain("9999.0.0").And.NotContain("INPUT:").And.NotContain("SELECT:");
        }
    }

    [Fact]
    public async Task StagedEntry_NativeHelpUsesExplicitPathAndFullGraphWithoutCreatingFiles()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = Path.Combine(_root, "help-project");
        var entry = new NativeCliEntryPoint();
        (await entry.ExecuteAsync(["init", project, "-s", "node", "-l", "js", "--no-bundles", "--skip-npm-install"],
            _interaction, _paths, new(Home), CancellationToken.None, configure: TrackEntryContexts, versionCheck: _ => Task.FromResult<string?>(null))).Should().Be(0);
        TestInteractionService output = new();
        string nested = Path.Combine(project, "help-nested");

        int exit = await entry.ExecuteAsync(["new", nested, "-t", "HttpTrigger-JavaScript", "--help"],
            output, _paths, new(Home), CancellationToken.None, configure: TrackEntryContexts, versionCheck: _ => Task.FromResult<string?>(null));

        exit.Should().Be(0, output.AllOutput);
        output.AllOutput.Should().Contain("[path]").And.Contain("--authlevel").And.Contain("anonymous");
        output.AllOutput.Should().NotContain("--dry-run").And.NotContain("--path").And.NotContain("INPUT:");
        Directory.Exists(nested).Should().BeFalse();

        TestInteractionService help = new();
        (await entry.ExecuteAsync(["help", "new"], help, _paths, new(Home), CancellationToken.None,
            configure: TrackEntryContexts, versionCheck: _ => Task.FromResult<string?>(null))).Should().Be(0);
        help.AllOutput.Should().Contain("[path]").And.NotContain("--dry-run");
    }

    [Theory]
    [InlineData("--unknown=value")]
    [InlineData("--authlevel=invalid")]
    [InlineData("--authlevel=anonymous", "--authlevel=function")]
    [InlineData("--name")]
    [InlineData("@not-a-response-file")]
    public async Task StagedEntry_InvalidTemplateInput_IsOneJsonFailureWithoutLegacyFallback(params string[] tail)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = Path.Combine(_root, "invalid-project");
        var entry = new NativeCliEntryPoint();
        (await entry.ExecuteAsync(["init", project, "-s", "node", "-l", "js", "--no-bundles", "--skip-npm-install"],
            _interaction, _paths, new(Home), CancellationToken.None, configure: TrackEntryContexts, versionCheck: _ => Task.FromResult<string?>(null))).Should().Be(0);
        TestInteractionService output = new();

        int exit = await entry.ExecuteAsync(["new", project, "-t", "HttpTrigger-JavaScript", "--output=json", "--non-interactive", .. tail],
            output, _paths, new(Home), CancellationToken.None, configure: TrackEntryContexts, versionCheck: _ => Task.FromResult<string?>(null));

        exit.Should().Be(1, output.AllOutput);
        using var json = JsonDocument.Parse(output.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
        json.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        File.Exists(Path.Combine(project, "httpTrigger.js")).Should().BeFalse();
        Directory.GetFiles(Path.Combine(project, "src", "functions")).Should().BeEmpty();
    }

    [Fact]
    public async Task StagedEntry_UnsupportedStatefulInitRefusesBeforeMutation()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string target = Path.Combine(_root, "owned");
        Directory.CreateDirectory(target);
        string source = Path.Combine(target, "user.txt");
        File.WriteAllText(source, "preserve");
        TestInteractionService output = new();

        int exit = await new NativeCliEntryPoint().ExecuteAsync(["init", target, "-s", "node", "-l", "js", "--force"],
            output, _paths, new(Home), CancellationToken.None, configure: TrackEntryContexts, versionCheck: _ => Task.FromResult<string?>(null));

        exit.Should().Be(1);
        File.ReadAllText(source).Should().Be("preserve");
        File.Exists(Path.Combine(target, ".func", "config.json")).Should().BeFalse();
        output.AllOutput.Should().Contain("fresh project creation only");
    }

    [Theory]
    [InlineData("host.json")]
    [InlineData(".func")]
    [InlineData(".git")]
    public async Task StagedEntry_FreshCohort_RefusesExistingMarkersWithoutReadingOrChangingStatefulConfiguration(string marker)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string target = Path.Combine(_root, "existing-marker");
        Directory.CreateDirectory(target);
        if (marker == "host.json") File.WriteAllText(Path.Combine(target, marker), """{"version":"2.0"}""");
        else if (marker == ".func")
        {
            Directory.CreateDirectory(Path.Combine(target, marker));
            File.WriteAllText(Path.Combine(target, marker, "config.json"), "{malformed-but-user-owned");
        }
        else Directory.CreateDirectory(Path.Combine(target, marker));
        var before = Directory.GetFiles(target, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText);
        string[] directoriesBefore = Directory.GetDirectories(target, "*", SearchOption.AllDirectories);
        TestInteractionService output = new();

        int exit = await new NativeCliEntryPoint().ExecuteAsync(["init", target, "-s", "node", "-l", "js"],
            output, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ => Task.FromResult<string?>(null));

        exit.Should().Be(1);
        output.AllOutput.Should().Contain("requires a new empty directory").And.NotContain("unexpected error");
        Directory.GetFiles(target, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText).Should().BeEquivalentTo(before);
        Directory.GetDirectories(target, "*", SearchOption.AllDirectories).Should().BeEquivalentTo(directoriesBefore);
        File.Exists(Path.Combine(target, "package.json")).Should().BeFalse();
    }

    private void TrackEntryContexts(IServiceCollection services) => _loadContexts.AddRange(
        services.Select(service => service.ImplementationInstance).OfType<RuntimeWorkloadInfo>().Select(workload => workload.LoadContext));

    [Fact]
    public async Task StagedEntry_InteractiveConsoleNonInteractiveJson_EmitsOnePhysicalDocumentWithoutPromptOrAdvisory()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = Path.Combine(_root, "json-project");
        var entry = new NativeCliEntryPoint();
        (await entry.ExecuteAsync(["init", project, "-s", "node", "-l", "js", "--no-bundles", "--skip-npm-install"],
            _interaction, _paths, new(Home), CancellationToken.None, configure: TrackEntryContexts,
            versionCheck: _ => Task.FromResult<string?>(null))).Should().Be(0);
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        SpectreInteractionService interaction = new(new DefaultTheme(), stdout, stderr);
        int checks = 0;

        int exit = await entry.ExecuteAsync(["new", project, "-t", "HttpTrigger-JavaScript", "--non-interactive", "--output=json"],
            interaction, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ =>
            {
                checks++;
                return Task.FromResult<string?>("9999.0.0");
            });

        exit.Should().Be(0, stderr.Output);
        using var document = JsonDocument.Parse(stdout.Output);
        document.RootElement.GetProperty("kind").GetString().Should().Be("created");
        checks.Should().Be(0);
        stderr.Reads.Should().Be(0);
        stdout.Output.Should().NotContain("9999.0.0").And.NotContain("Run `func setup`");
    }

    [Fact]
    public async Task StagedEntry_ActualCommittedItemThenCancellation_RendersAuthoritativeSuccessAcrossFullEntry()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = Path.Combine(_root, "completion-project");
        var entry = new NativeCliEntryPoint();
        (await entry.ExecuteAsync(["init", project, "-s", "node", "-l", "js", "--no-bundles", "--skip-npm-install"],
            _interaction, _paths, new(Home), CancellationToken.None, TrackEntryContexts,
            _ => Task.FromResult<string?>(null))).Should().Be(0);
        using CancellationTokenSource cancellation = new();
        TestInteractionService output = new();

        int exit = await entry.ExecuteAsync(["new", project, "-t", "HttpTrigger-JavaScript", "--non-interactive", "--output=json"],
            output, _paths, new(Home), cancellation.Token, services =>
            {
                TrackEntryContexts(services);
                services.AddScoped<INewExecutionRunner>(provider => new CancelAfterRealItemRunner(
                    ActivatorUtilities.CreateInstance<NewExecutionRunner>(provider), cancellation));
            }, _ => Task.FromResult<string?>(null));

        exit.Should().Be(0, output.AllOutput);
        cancellation.IsCancellationRequested.Should().BeTrue();
        File.Exists(Path.Combine(project, "src", "functions", "httpTrigger.js")).Should().BeTrue();
        using var document = JsonDocument.Parse(output.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
        document.RootElement.GetProperty("kind").GetString().Should().Be("created");
        document.RootElement.GetProperty("succeeded").GetBoolean().Should().BeTrue();
    }

    private sealed class CancelAfterRealItemRunner(INewExecutionRunner inner, CancellationTokenSource cancellation) : INewExecutionRunner
    {
        public async Task<NewExecutionResult> ExecuteAsync(NewExecutionRequest request, TemplateConstraintEvaluationSnapshot availability,
            CancellationToken cancellationToken)
        {
            NewExecutionResult result = await inner.ExecuteAsync(request, availability, cancellationToken);
            result.HasCompletedCreation.Should().BeTrue();
            cancellation.Cancel();
            return result;
        }
    }

    [Fact]
    public async Task Program_DefaultPolicy_RemainsLegacyAndDoesNotExposeStagedForceRefusal()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string target = Path.Combine(_root, "default-process-project");
        Directory.CreateDirectory(target);
        string userFile = Path.Combine(target, "temporary-test-owned.txt");
        File.WriteAllText(userFile, "only this test owns this temporary source");
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(60));

        var result = await SetupProgramAdvisoryTests.RunCliAsync(Path.Combine(_root, "default-process"), Home, _paths.Home, target,
            ["init", target, "-s", "node", "-l", "js", "--force", "--no-bundles", "--skip-npm-install"], cancellation.Token);

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        result.StandardOutput.Should().Contain("Project initialized for").And.NotContain("staged entry");
        result.StandardError.Should().NotContain("fresh project creation only");
        File.Exists(userFile).Should().BeFalse();
        File.Exists(Path.Combine(target, "package.json")).Should().BeTrue();
    }
}
