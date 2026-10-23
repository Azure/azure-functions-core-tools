// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Tests.Console;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData(true, "--format=json")]
    [InlineData(true, "--format", "JSON", "--non-interactive=false")]
    [InlineData(true, "--format=plain", "--non-interactive")]
    [InlineData(false, "--param:Output=json")]
    [InlineData(false, "--greeting=--output=json")]
    [InlineData(false, "--format=plain", "--non-interactive=false")]
    public void StagedReview_EffectivePromptPolicy_UsesParsedHostOptionsOnly(bool nonInteractive, params string[] options)
    {
        NewCommand command = new(Substitute.For<INewCommandRunner>(), Substitute.For<INewCommandTemplateOptionProvider>());
        command.PathArgument!.CustomParser = _ => WorkingDirectory.FromCwd();
        FuncRootCommand root = [command];
        var parsed = root.Parse(["new", .. options], new() { EnablePosixBundling = false, ResponseFileTokenReplacer = null });
        parsed.Errors.Should().BeEmpty();

        NativeEntryCommandBinding.IsNonInteractive(command, parsed).Should().Be(nonInteractive);
    }

    [Fact]
    public void StagedReview_ParsedHostPolicy_RejectsNullInputs()
    {
        NewCommand command = new(Substitute.For<INewCommandRunner>(), Substitute.For<INewCommandTemplateOptionProvider>());
        FuncRootCommand root = [command];
        var parsed = root.Parse(["new"]);
        Action[] calls =
        [
            () => NativeEntryCommandBinding.IsJsonOutput(null!, parsed),
            () => NativeEntryCommandBinding.IsJsonOutput(command, null!),
            () => NativeEntryCommandBinding.IsNonInteractive(null!, parsed),
            () => NativeEntryCommandBinding.IsNonInteractive(command, null!),
        ];

        foreach (var call in calls)
        {
            call.Should().Throw<ArgumentNullException>();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StagedReview_JsonOnlyCreation_WithCompleteInput_EmitsOneDocumentWithoutPrompt(bool explicitInteractive)
    {
        string project = await CreateReviewProjectAsync();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        SpectreInteractionService interaction = new(new DefaultTheme(), stdout, stderr);
        string[] options = explicitInteractive ? ["--non-interactive=false"] : [];
        int checks = 0;

        int exit = await new NativeCliEntryPoint().ExecuteAsync(
            ["new", project, "-t", "HttpTrigger-JavaScript", "--format=json", .. options],
            interaction, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ =>
            {
                checks++;
                return Task.FromResult<string?>("9999.0.0");
            });

        exit.Should().Be(0, stderr.Output);
        File.Exists(Path.Combine(project, "src", "functions", "httpTrigger.js")).Should().BeTrue();
        using var document = JsonDocument.Parse(stdout.Output);
        document.RootElement.GetProperty("kind").GetString().Should().Be("created");
        document.RootElement.GetProperty("succeeded").GetBoolean().Should().BeTrue();
        stdout.Reads.Should().Be(0);
        stderr.Reads.Should().Be(0);
        checks.Should().Be(0);
        stderr.Output.Should().BeEmpty();
    }

    [Theory]
    [InlineData("template", "templateRequired")]
    [InlineData("profile", "invalidProfile")]
    [InlineData("required", "missingRequired")]
    public async Task StagedReview_JsonAloneInInteractiveTerminal_NeverPromptsAtAnyCompletionBoundary(string boundary, string kind)
    {
        string project = await CreateReviewProjectAsync(requiredValue: boundary == "required");
        string[] selector = ["-t", "HttpTrigger-JavaScript"];
        if (boundary == "template")
        {
            selector = [];
        }
        else if (boundary == "profile")
        {
            string path = Path.Combine(project, ".func", "config.json");
            var configuration = JsonNode.Parse(File.ReadAllText(path))!;
            configuration["profiles"] = new JsonArray("flex", "linux-premium");
            configuration.AsObject().Remove("defaultProfile");
            File.WriteAllText(path, configuration.ToJsonString());
        }

        var before = Directory.GetFiles(project, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        SpectreInteractionService interaction = new(new DefaultTheme(), stdout, stderr);
        interaction.IsInteractive.Should().BeTrue();
        int checks = 0;

        int exit = await new NativeCliEntryPoint().ExecuteAsync(["new", project, .. selector, "--format=JSON"],
            interaction, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ =>
            {
                checks++;
                return Task.FromResult<string?>("9999.0.0");
            });

        Directory.GetFiles(project, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes)
            .Should().BeEquivalentTo(before);
        stderr.Reads.Should().Be(0, $"JSON must suppress the {boundary} prompt before it is attempted; stderr: {stderr.Output}");
        stdout.Reads.Should().Be(0);
        exit.Should().Be(1);
        using var document = JsonDocument.Parse(stdout.Output);
        document.RootElement.GetProperty("kind").GetString().Should().Be(kind);
        document.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        checks.Should().Be(0);
        stderr.Output.Should().NotContain("unexpected error").And.NotContain("func5");
    }

    [Theory]
    [InlineData("host-version")]
    [InlineData("unowned-source")]
    public async Task StagedReview_UnadmittedHostOrSource_RefusesWithoutPromptOrProjectWrites(string refusal)
    {
        string project = await CreateReviewProjectAsync();
        string template = "HttpTrigger-JavaScript";
        if (refusal == "unowned-source")
        {
            string package = Path.Combine(_root, "review-required-template");
            Directory.CreateDirectory(Path.Combine(package, ".template.config"));
            File.WriteAllText(Path.Combine(package, ".template.config", "template.json"), """
                {
                  "identity":"Func.Tests.ReviewRequired","name":"Review required item","shortName":"review-required",
                  "tags":{"type":"item","language":"JavaScript"},
                  "symbols":{"RequiredValue":{"type":"parameter","datatype":"string","isRequired":true}}
                }
                """);
            File.WriteAllText(Path.Combine(package, "review.txt"), "required-value fixture");
            await TemplateFixtures.InstallAsync(package, Path.Combine(Home, "templates"), CancellationToken.None);
            template = "Func.Tests.ReviewRequired";
        }
        var before = Directory.GetFiles(project, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        SpectreInteractionService interaction = new(new DefaultTheme(), stdout, stderr);
        int checks = 0;

        int exit = await new NativeCliEntryPoint().ExecuteAsync(["new", project, "-t", template, "--format=json"],
            interaction, _paths, new(Home), CancellationToken.None, services =>
            {
                TrackEntryContexts(services);
                if (refusal == "host-version") services.AddSingleton(ReviewPrereleaseVersion("5.0.0-review"));
            }, _ =>
            {
                checks++;
                return Task.FromResult<string?>("9999.0.0");
            });

        exit.Should().Be(1);
        using var document = JsonDocument.Parse(stdout.Output);
        document.RootElement.GetProperty("kind").GetString().Should().Be("workflowUnavailable");
        document.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        Directory.GetFiles(project, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes)
            .Should().BeEquivalentTo(before);
        stdout.Reads.Should().Be(0);
        stderr.Reads.Should().Be(0);
        checks.Should().Be(0);
        if (refusal == "host-version")
        {
            stdout.Output.Should().Contain("does not support the actual").And.Contain("5.0.0-review");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StagedReview_PlainNonInteractiveCreation_DoesNotStartOrEmitAdvisories(bool interactive, bool fail)
    {
        string project = await CreateReviewProjectAsync();
        using BufferedConsole stdout = new(interactive, interactive, noColor: true);
        using BufferedConsole stderr = new(interactive, interactive, noColor: true);
        SpectreInteractionService interaction = new(new DefaultTheme(), stdout, stderr);
        int checks = 0;

        int exit = await new NativeCliEntryPoint().ExecuteAsync(
            ["new", project, "-t", fail ? "missing-review-template" : "HttpTrigger-JavaScript", "--non-interactive", "--format=plain"],
            interaction, _paths, new(Home), CancellationToken.None, services =>
            {
                TrackEntryContexts(services);
                services.AddSingleton(ReviewPrereleaseVersion());
            }, _ =>
            {
                checks++;
                return Task.FromResult<string?>("9999.0.0");
            });

        exit.Should().Be(fail ? 1 : 0, stderr.Output);
        File.Exists(Path.Combine(project, "src", "functions", "httpTrigger.js")).Should().Be(!fail);
        stdout.Reads.Should().Be(0);
        stderr.Reads.Should().Be(0);
        checks.Should().Be(0, "an explicit no-prompt invocation must not even start the version check");
        stdout.Output.Should().NotContain("9999.0.0").And.NotContain("func5");
        stderr.Output.Should().NotContain("9999.0.0").And.NotContain("func5");
        if (fail)
        {
            stdout.Output.Should().BeEmpty();
            stderr.Output.Should().Contain("missing-review-template");
        }
    }

    [Fact]
    public async Task StagedReview_PlainInteractiveCreation_PreservesPickerAndAdvisories()
    {
        string project = await CreateReviewProjectAsync();
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        stderr.Enqueue(ConsoleKey.Enter, '\r');
        SpectreInteractionService interaction = new(new DefaultTheme(), stdout, stderr);
        int checks = 0;

        int exit = await new NativeCliEntryPoint().ExecuteAsync(["new", project, "--format=plain"],
            interaction, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ =>
            {
                checks++;
                return Task.FromResult<string?>("9999.0.0");
            });

        exit.Should().Be(0, stderr.Output);
        stderr.Reads.Should().BeGreaterThan(0);
        stderr.Output.Should().Contain("Select an item-template family");
        checks.Should().Be(1);
        stdout.Output.Should().Contain("9999.0.0");
        Directory.GetFiles(Path.Combine(project, "src", "functions")).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("workload", "install")]
    [InlineData("profile", "set")]
    public async Task StagedReview_BareParentThroughFullEntry_RendersHelpLikeExplicitHelp(string command, string child)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        TestInteractionService bare = new();
        TestInteractionService explicitHelp = new();
        var entry = new NativeCliEntryPoint();

        int bareExit = await entry.ExecuteAsync([command], bare, _paths, new(Home), CancellationToken.None,
            TrackEntryContexts, _ => Task.FromResult<string?>(null));
        int explicitExit = await entry.ExecuteAsync([command, "--help"], explicitHelp, _paths, new(Home), CancellationToken.None,
            TrackEntryContexts, _ => Task.FromResult<string?>(null));

        bareExit.Should().Be(0);
        explicitExit.Should().Be(0);
        explicitHelp.AllOutput.Should().Contain("func " + command).And.Contain(child);
        bare.AllOutput.Should().Be(explicitHelp.AllOutput);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("start")]
    public async Task StagedReview_NonCreationCommands_PreserveAdvisoryPolicy(string command)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        SpectreInteractionService interaction = new(new DefaultTheme(), stdout, stderr);
        int checks = 0;

        int exit = await new NativeCliEntryPoint().ExecuteAsync([command, "--help"], interaction, _paths, new(Home),
            CancellationToken.None, TrackEntryContexts, _ =>
            {
                checks++;
                return Task.FromResult<string?>("9999.0.0");
            });

        exit.Should().Be(0, stderr.Output);
        checks.Should().Be(1);
        stdout.Output.Should().Contain("9999.0.0").And.Contain(command);
        stderr.Reads.Should().Be(0);
    }

    private async Task<string> CreateReviewProjectAsync(bool requiredValue = false)
    {
        await InstallEntryFunctionJourneyAsync(requiredValue ? ReviewRequiredFunctionArchive() : null);
        string project = Path.Combine(_root, "review-project");
        int exit = await new NativeCliEntryPoint().ExecuteAsync(
            ["init", project, "-s", "node", "-l", "js", "--no-bundles", "--skip-npm-install"],
            _interaction, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ => Task.FromResult<string?>(null));
        exit.Should().Be(0, _interaction.AllOutput);
        return project;
    }

    private string ReviewRequiredFunctionArchive()
    {
        string archive = Path.Combine(_root, "review-required-functions.nupkg");
        File.Copy(FunctionArchive, archive);
        using var package = ZipFile.Open(archive, ZipArchiveMode.Update);
        const string path = "content/http-js/.template.config/template.json";
        var configurationEntry = package.GetEntry(path)!;
        JsonNode configuration;
        using (Stream input = configurationEntry.Open()) configuration = JsonNode.Parse(input)!;
        configuration["symbols"]!["RequiredValue"] = JsonNode.Parse(
            """{"type":"parameter","datatype":"string","isRequired":true}""");
        byte[] content = Encoding.UTF8.GetBytes(configuration.ToJsonString());
        configurationEntry.Delete();
        using (Stream output = package.CreateEntry(path).Open()) output.Write(content);
        var provenanceEntry = package.GetEntry("provenance/node/compatibility.json")!;
        JsonNode provenance;
        using (Stream input = provenanceEntry.Open()) provenance = JsonNode.Parse(input)!;
        UpdateFixtureSourcePins(provenance, new Dictionary<string, byte[]>(StringComparer.Ordinal) { [path] = content })
            .Should().Be(1);
        provenanceEntry.Delete();
        using (StreamWriter output = new(package.CreateEntry("provenance/node/compatibility.json").Open()))
        {
            output.Write(provenance.ToJsonString());
        }
        return archive;
    }

    private static ICliVersionProvider ReviewPrereleaseVersion(string? value = null)
    {
        string actual = value ?? AssemblyCliVersionProvider.Instance.Version;
        var version = Substitute.For<ICliVersionProvider>();
        version.Version.Returns(actual);
        version.InformationalVersion.Returns(actual);
        version.IsPrerelease.Returns(true);
        return version;
    }
}
