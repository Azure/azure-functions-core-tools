// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.Json;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Commands.Setup;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("en-US", "-1", "before")]
    [InlineData("en-US", "-1", "inline")]
    [InlineData("en-US", "-1", "pathFirst")]
    [InlineData("fr-FR", "-1,25", "before")]
    [InlineData("fr-FR", "-1,25", "inline")]
    [InlineData("fr-FR", "-1,25", "pathFirst")]
    [InlineData("fr-FR", "-1.25", "before")]
    [InlineData("en-US", "-1e-3", "before")]
    public async Task StagedEntry_SignedNumericScalar_CreatesAtExplicitPathWithOriginalValue(string culture, string value, string layout)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            string project = await CreateReviewProjectAsync();
            string[] scalar = layout == "inline" ? ["--timer-trigger-schedule=" + value] : ["--timer-trigger-schedule", value];
            string[] tail = layout == "pathFirst" ? [project, .. scalar] : [.. scalar, project];
            string[] arguments = ["new", .. tail, "-t", "TimerTrigger-JavaScript", "--name", "SignedScalar",
                "--non-interactive", "--format=json"];
            string[] original = [.. arguments];
            TestInteractionService output = new();
            int checks = 0;

            int exit = await new NativeCliEntryPoint().ExecuteAsync(arguments, output, _paths, new(Home), CancellationToken.None,
                TrackEntryContexts, _ =>
                {
                    checks++;
                    return Task.FromResult<string?>("9999.0.0");
                });

            exit.Should().Be(0, output.AllOutput);
            File.ReadAllText(Path.Combine(project, "src", "functions", "SignedScalar.js")).Should().Contain("app.timer('SignedScalar'");
            using var json = JsonDocument.Parse(output.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
            json.RootElement.GetProperty("kind").GetString().Should().Be("created");
            json.RootElement.GetProperty("executionDirectory").GetString().Should().Be(project);
            json.RootElement.GetProperty("outputDirectory").GetString().Should().Be(project);
            json.RootElement.GetProperty("argumentResults")[0].GetProperty("valuesByCanonicalName")
                .GetProperty("timerTrigger-schedule").GetString().Should().Be(value);
            json.RootElement.GetProperty("invocation").GetProperty("changes")[0].GetProperty("path").GetString()
                .Should().Be("src/functions/SignedScalar.js");
            checks.Should().Be(0);
            output.AllOutput.Should().NotContain("INPUT:").And.NotContain("SELECT:").And.NotContain("9999.0.0");
            _source.ReceivedCalls().Should().BeEmpty();
            arguments.Should().Equal(original);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("separateLiteral", false)]
    [InlineData("inlineLiteral", true)]
    [InlineData("unknown", false)]
    [InlineData("missingValue", false)]
    public async Task StagedEntry_ScalarBoundaryControls_DoNotBroadenOptionOrHostPolicy(string scenario, bool succeeds)
    {
        string project = await CreateReviewProjectAsync();
        string[] scalar = scenario switch
        {
            "separateLiteral" => ["--timer-trigger-schedule", "--not-a-number"],
            "inlineLiteral" => ["--timer-trigger-schedule=--not-a-number"],
            "unknown" => ["--unknown", "-1"],
            _ => ["--timer-trigger-schedule"],
        };
        TestInteractionService output = new();

        int exit = await new NativeCliEntryPoint().ExecuteAsync(
            ["new", project, .. scalar, "-t", "TimerTrigger-JavaScript", "--name", "ScalarControl", "--non-interactive", "--format=json"],
            output, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ => Task.FromResult<string?>(null));

        exit.Should().Be(succeeds ? 0 : 1, output.AllOutput);
        using var json = JsonDocument.Parse(output.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
        json.RootElement.GetProperty("succeeded").GetBoolean().Should().Be(succeeds);
        json.RootElement.GetProperty("executionDirectory").GetString().Should().Be(project);
        File.Exists(Path.Combine(project, "src", "functions", "ScalarControl.js")).Should().Be(succeeds);
        if (!succeeds)
        {
            Directory.GetFiles(Path.Combine(project, "src", "functions")).Should().BeEmpty();
            json.RootElement.GetProperty("kind").GetString().Should().Be("invalidArguments");
        }
        output.AllOutput.Should().NotContain("INPUT:").And.NotContain("SELECT:");
        _source.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("JavaScript", "js", ".js")]
    [InlineData("TypeScript", "ts", ".ts")]
    public async Task StagedEntry_FullPublicGrammar_InitAndRootOrNestedNewPreserveLegacyNodePlacement(
        string language, string alias, string extension)
    {
        await InstallEntryFunctionJourneyAsync();
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
                "-t", "HttpTrigger-" + language, "--non-interactive", "--format", "json"],
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
        await InstallEntryFunctionJourneyAsync();
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
        await InstallEntryFunctionJourneyAsync();
        string project = Path.Combine(_root, "invalid-project");
        var entry = new NativeCliEntryPoint();
        (await entry.ExecuteAsync(["init", project, "-s", "node", "-l", "js", "--no-bundles", "--skip-npm-install"],
            _interaction, _paths, new(Home), CancellationToken.None, configure: TrackEntryContexts, versionCheck: _ => Task.FromResult<string?>(null))).Should().Be(0);
        TestInteractionService output = new();

        int exit = await entry.ExecuteAsync(["new", project, "-t", "HttpTrigger-JavaScript", "--format=json", "--non-interactive", .. tail],
            output, _paths, new(Home), CancellationToken.None, configure: TrackEntryContexts, versionCheck: _ => Task.FromResult<string?>(null));

        exit.Should().Be(1, output.AllOutput);
        using var json = JsonDocument.Parse(output.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
        json.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        File.Exists(Path.Combine(project, "httpTrigger.js")).Should().BeFalse();
        Directory.GetFiles(Path.Combine(project, "src", "functions")).Should().BeEmpty();
    }

    [Fact]
    public async Task PublicEntry_DeclinedForceRefusesBeforeMutation()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string target = Path.Combine(_root, "owned");
        Directory.CreateDirectory(target);
        string source = Path.Combine(target, "user.txt");
        File.WriteAllText(source, "preserve");
        InteractivePublicEntryOutput output = new();

        int exit = await new NativeCliEntryPoint().ExecuteAsync(["init", target, "-s", "node", "-l", "js", "--force"],
            output, _paths, new(Home), CancellationToken.None, configure: TrackEntryContexts, versionCheck: _ => Task.FromResult<string?>(null));

        exit.Should().Be(1);
        File.ReadAllText(source).Should().Be("preserve");
        File.Exists(Path.Combine(target, ".func", "config.json")).Should().BeFalse();
        output.AllOutput.Should().Contain("Init cancelled").And.Contain("default: False");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicEntry_ExistingPackage_PreservesOrCleansBeforeOwnedPreparation(bool force)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string target = Directory.CreateDirectory(Path.Combine(_root, "prepared-existing")).FullName;
        string package = Path.Combine(target, "package.json");
        byte[] original = System.Text.Encoding.UTF8.GetBytes("""{"name":"user-owned","main":"custom.js"}""");
        File.WriteAllBytes(package, original);
        string extra = Path.Combine(target, "custom.js");
        File.WriteAllText(extra, "user-owned input");
        string git = Directory.CreateDirectory(Path.Combine(target, ".git")).FullName;
        File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/keep-history");
        TestInteractionService output = new();

        int exit = await new NativeCliEntryPoint().ExecuteAsync(
            ["init", target, "-s", "node", "-l", "js", "--no-bundles", "--skip-npm-install",
                .. force ? new[] { "--force" } : []],
            output, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ => Task.FromResult<string?>(null));

        exit.Should().Be(0, output.AllOutput);
        File.ReadAllText(Path.Combine(git, "HEAD")).Should().Be("ref: refs/heads/keep-history");
        File.Exists(Path.Combine(target, ".func", "config.json")).Should().BeTrue();
        File.Exists(extra).Should().Be(!force);
        if (force)
        {
            File.ReadAllBytes(package).Should().NotEqual(original);
        }
        else
        {
            File.ReadAllBytes(package).Should().Equal(original);
            File.ReadAllText(extra).Should().Be("user-owned input");
        }
        output.AllOutput.Should().Contain("Skipped npm installation").And.NotContain("did not complete");
    }

    [Theory]
    [InlineData("host.json")]
    [InlineData(".func")]
    [InlineData(".git")]
    public async Task PublicEntry_ExistingMarkers_UseAdoptionOrCreationAndPreserveUserBytes(string marker)
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

        int exit = await new NativeCliEntryPoint().ExecuteAsync(["init", target, "-s", "node", "-l", "js", "--skip-npm-install"],
            output, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ => Task.FromResult<string?>(null));

        exit.Should().Be(marker == ".func" ? 1 : 0, output.AllOutput);
        output.AllOutput.Should().NotContain("unexpected error");
        foreach (var file in before) File.ReadAllText(file.Key).Should().Be(file.Value);
        foreach (string directory in directoriesBefore) Directory.Exists(directory).Should().BeTrue();
        File.Exists(Path.Combine(target, "package.json")).Should().Be(marker == ".git");
        if (marker == ".func") output.AllOutput.Should().Contain("Correct its JSON");
        if (marker == "host.json") output.AllOutput.Should().Contain("Existing project adopted");
    }

    private async Task InstallEntryFunctionJourneyAsync()
    {
        await InstallOwnedNodeJourneyAsync();
        _source.ClearReceivedCalls();
    }

    private void TrackEntryContexts(IServiceCollection services)
    {
        _loadContexts.AddRange(services.Select(service => service.ImplementationInstance)
            .OfType<RuntimeWorkloadInfo>().Select(workload => workload.LoadContext));
        if (_ownedJourneyCatalog is { } catalog)
        {
            services.AddSingleton<IBasicCompanionCatalog>(catalog);
            services.AddSingleton<BasicAcquisitionPlanner>();
        }
    }

    [Fact]
    public async Task StagedEntry_InteractiveConsoleNonInteractiveJson_EmitsOnePhysicalDocumentWithoutPromptOrAdvisory()
    {
        await InstallEntryFunctionJourneyAsync();
        string project = Path.Combine(_root, "json-project");
        var entry = new NativeCliEntryPoint();
        (await entry.ExecuteAsync(["init", project, "-s", "node", "-l", "js", "--no-bundles", "--skip-npm-install"],
            _interaction, _paths, new(Home), CancellationToken.None, configure: TrackEntryContexts,
            versionCheck: _ => Task.FromResult<string?>(null))).Should().Be(0);
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        SpectreInteractionService interaction = new(new DefaultTheme(), stdout, stderr);
        int checks = 0;

        int exit = await entry.ExecuteAsync(["new", project, "-t", "HttpTrigger-JavaScript", "--non-interactive", "--format=json"],
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
        await InstallEntryFunctionJourneyAsync();
        string project = Path.Combine(_root, "completion-project");
        var entry = new NativeCliEntryPoint();
        (await entry.ExecuteAsync(["init", project, "-s", "node", "-l", "js", "--no-bundles", "--skip-npm-install"],
            _interaction, _paths, new(Home), CancellationToken.None, TrackEntryContexts,
            _ => Task.FromResult<string?>(null))).Should().Be(0);
        using CancellationTokenSource cancellation = new();
        TestInteractionService output = new();

        int exit = await entry.ExecuteAsync(["new", project, "-t", "HttpTrigger-JavaScript", "--non-interactive", "--format=json"],
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
    public async Task Program_DefaultPolicy_UsesNativeForceCreation()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string target = Path.Combine(_root, "default-process-project");
        Directory.CreateDirectory(target);
        string userFile = Path.Combine(target, "temporary-test-owned.txt");
        File.WriteAllText(userFile, "only this test owns this temporary source");
        Directory.CreateDirectory(Path.Combine(target, ".git"));
        File.WriteAllText(Path.Combine(target, ".git", "HEAD"), "ref: refs/heads/user-branch");
        Directory.CreateDirectory(Path.Combine(target, ".func"));
        File.WriteAllText(Path.Combine(target, ".func", "config.json"), "{malformed user configuration");
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(60));

        var result = await SetupProgramAdvisoryTests.RunCliAsync(Path.Combine(_root, "default-process"), Home, _paths.Home, target,
            ["init", target, "-s", "node", "-l", "js", "--force", "--no-bundles", "--skip-npm-install"], cancellation.Token);

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        result.StandardOutput.Should().Contain("Initialized project template 'Azure.Functions.Node.Basic.JavaScript'").And.NotContain("staged entry");
        result.StandardError.Should().NotContain("fresh project creation only");
        File.Exists(userFile).Should().BeFalse();
        File.Exists(Path.Combine(target, "package.json")).Should().BeTrue();
        File.ReadAllText(Path.Combine(target, ".git", "HEAD")).Should().Be("ref: refs/heads/user-branch");
    }

    private sealed class InteractivePublicEntryOutput : TestInteractionService
    {
        public override bool IsInteractive => true;
    }
}
