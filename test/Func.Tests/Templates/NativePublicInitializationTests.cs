// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Tests.Commands.Setup;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("js", "JavaScript", ".js")]
    [InlineData("ts", "TypeScript", ".ts")]
    public async Task Program_NonemptyInitThenNativeNew_PreservesUserFilesAndPublicPlacement(string alias, string language, string extension)
    {
        await _installer.InstallFromPackageAsync(CombinedNodeFixtureArchive());
        string project = Path.Combine(_root, "public-program-" + alias);
        Directory.CreateDirectory(Path.Combine(project, ".git"));
        File.WriteAllText(Path.Combine(project, ".git", "HEAD"), "ref: refs/heads/user-branch");
        File.WriteAllText(Path.Combine(project, ".gitignore"), "user-owned-ignore\n");
        File.WriteAllText(Path.Combine(project, "readme.txt"), "user-owned-readme");
        File.WriteAllText(Path.Combine(project, "package.json"),
            """{"name":"user-owned","main":"src/functions/*.js","dependencies":{"@azure/functions":"^4.0.0"}}""");
        var original = PublicSnapshot(project);
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        string processHome = Path.Combine(_root, "public-process");

        var initialized = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project,
            ["init", project, "-s", "node", "-l", alias, "--no-bundles", "--skip-npm-install"], timeout.Token);

        initialized.ExitCode.Should().Be(0, initialized.StandardOutput + initialized.StandardError);
        initialized.StandardOutput.Should().Contain("Azure.Functions.Node.Basic." + language).And.Contain("Preserve:");
        foreach (var file in original) File.ReadAllBytes(file.Key).Should().Equal(file.Value);
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, ".func", "config.json")));
        configuration.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be(language);
        var beforeList = PublicSnapshot(project);
        var listed = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project,
            ["new", project, "--list", "--format=json"], timeout.Token);
        listed.ExitCode.Should().Be(0, listed.StandardError);
        listed.StandardError.Should().BeEmpty();
        using var catalog = JsonDocument.Parse(listed.StandardOutput);
        catalog.RootElement.GetProperty("kind").GetString().Should().Be("listed");
        catalog.RootElement.GetProperty("templates").EnumerateArray().Should().Contain(template =>
            template.GetProperty("identity").GetString() == "Azure.Functions.Node.Http." + language);
        PublicSnapshot(project).Should().BeEquivalentTo(beforeList);

        string nested = Path.Combine(project, "nested");
        var created = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project,
            ["--verbose=true", "new", "--authlevel=anonymous", nested, "-t", "HttpTrigger-" + language,
                "-n", "NativeGreeting", "--non-interactive", "--format=json"], timeout.Token);
        created.ExitCode.Should().Be(0, created.StandardError);
        created.StandardError.Should().BeEmpty();
        using var result = JsonDocument.Parse(created.StandardOutput);
        result.RootElement.GetProperty("kind").GetString().Should().Be("created");
        result.RootElement.GetProperty("selectedTemplate").GetProperty("identity").GetString().Should()
            .Be("Azure.Functions.Node.Http." + language);
        File.ReadAllText(Path.Combine(nested, "src", "functions", "NativeGreeting" + extension))
            .Should().Contain("app.http('NativeGreeting'", "authLevel: 'anonymous'");
        File.Exists(Path.Combine(nested, "NativeGreeting" + extension)).Should().BeFalse();
        var beforeRepeat = PublicSnapshot(project);
        var repeated = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project,
            ["init", project, "-s", "node", "-l", alias], timeout.Token);
        repeated.ExitCode.Should().Be(1);
        repeated.StandardError.Should().Contain("--force");
        PublicSnapshot(project).Should().BeEquivalentTo(beforeRepeat);
        var forced = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project,
            ["init", project, "-s", "node", "-l", alias, "--force", "--no-bundles", "--skip-npm-install"], timeout.Token);
        forced.ExitCode.Should().Be(0, forced.StandardOutput + forced.StandardError);
        forced.StandardOutput.Should().Contain("Initialized project template");
        File.ReadAllText(Path.Combine(project, ".git", "HEAD")).Should().Be("ref: refs/heads/user-branch");
        File.Exists(Path.Combine(project, "readme.txt")).Should().BeFalse();
        Directory.Exists(nested).Should().BeFalse();
        File.ReadAllText(Path.Combine(project, "package.json")).Should().NotContain("user-owned");
        _source.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Program_AdoptionAndHealing_OnlyPersistConfigurationAndRefuseRepeat(bool heal, bool unresolved)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = Path.Combine(_root, "existing");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "host.json"), """{"version":"2.0","user":"keep"}""");
        File.WriteAllText(Path.Combine(project, "local.settings.json"), """{"Values":{"FUNCTIONS_WORKER_RUNTIME":"node"}}""");
        File.WriteAllText(Path.Combine(project, "user.txt"), "preserve source");
        if (!unresolved)
        {
            File.WriteAllText(Path.Combine(project, "package.json"), """{"main":"dist/src/functions/*.js"}""");
            File.WriteAllText(Path.Combine(project, "tsconfig.json"), "{}");
        }
        JsonNode? originalConfiguration = null;
        if (heal)
        {
            Directory.CreateDirectory(Path.Combine(project, ".func"));
            const string config = """{"stack":{"runtime":"node","user":"keep"},"profiles":["flex"],"custom":{"value":42}}""";
            File.WriteAllText(Path.Combine(project, ".func", "config.json"), config);
            originalConfiguration = JsonNode.Parse(config);
        }
        var before = PublicSnapshot(project);
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(1));
        string processHome = Path.Combine(_root, "adoption-process");

        var adopted = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project,
            ["init", project, "--name", "Ignored", "--skip-npm-install"], timeout.Token);

        adopted.ExitCode.Should().Be(0, adopted.StandardOutput + adopted.StandardError);
        adopted.StandardOutput.Should().Contain(heal ? "Existing project repaired" : "Existing project adopted");
        adopted.StandardOutput.Should().NotContain("Initialized project template");
        foreach (var file in before.Where(file => !file.Key.EndsWith(Path.Combine(".func", "config.json"), StringComparison.Ordinal)))
        {
            File.ReadAllBytes(file.Key).Should().Equal(file.Value);
        }
        var actual = JsonNode.Parse(File.ReadAllText(Path.Combine(project, ".func", "config.json")))!;
        actual["stack"]!["runtime"]!.GetValue<string>().Should().Be("node");
        if (unresolved) actual["stack"]!.AsObject().Should().NotContainKey("language");
        else actual["stack"]!["language"]!.GetValue<string>().Should().Be("TypeScript");
        if (heal)
        {
            actual["stack"]!.AsObject().Remove("language");
            JsonNode.DeepEquals(actual, originalConfiguration).Should().BeTrue();
        }
        var completed = PublicSnapshot(project);
        var repeated = await SetupProgramAdvisoryTests.RunCliAsync(processHome, Home, _paths.Home, project,
            ["init", project], timeout.Token);
        repeated.ExitCode.Should().Be(1);
        PublicSnapshot(project).Should().BeEquivalentTo(completed);
        _source.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("invalidInput")]
    [InlineData("declined")]
    [InlineData("cancelAtConfirmation")]
    [InlineData("cancelBeforeCleanup")]
    public async Task PublicEntry_ForcePrecommitControls_PreserveAllUserBytes(string scenario)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string target = Path.Combine(_root, "force-control");
        Directory.CreateDirectory(Path.Combine(target, ".func"));
        File.WriteAllText(Path.Combine(target, ".func", "config.json"), "{user-owned malformed configuration");
        File.WriteAllText(Path.Combine(target, "user.txt"), "do not delete on refusal or cancellation");
        var before = PublicSnapshot(target);
        using CancellationTokenSource cancellation = new();
        var interaction = NSubstitute.Substitute.For<Azure.Functions.Cli.Console.IInteractionService>();
        interaction.IsInteractive.Returns(true);
        interaction.ConfirmAsync("Continue?", false, true, NSubstitute.Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (scenario == "cancelAtConfirmation") cancellation.Cancel();
            return Task.FromResult(scenario != "declined");
        });
        var targetInspector = new CancelBeforePublicCleanup(_files, cancellation, scenario == "cancelBeforeCleanup");
        string[] arguments = ["init", target, "-s", "node", "-l", "js", "--force", "--skip-npm-install"];
        if (scenario == "invalidInput") arguments = [.. arguments, "--unknown-option", "bad"];

        int exit = await new NativeCliEntryPoint().ExecuteAsync(arguments, interaction, _paths, new(Home), cancellation.Token, services =>
        {
            TrackEntryContexts(services);
            services.AddSingleton<InitExecutionTargetInspector>(targetInspector);
        }, _ => Task.FromResult<string?>(null));

        exit.Should().Be(scenario.StartsWith("cancel", StringComparison.Ordinal) ? 130 : 1);
        PublicSnapshot(target).Should().BeEquivalentTo(before);
        targetInspector.Cleared.Should().BeFalse();
        _source.ReceivedCalls().Should().BeEmpty();
    }

    private sealed class CancelBeforePublicCleanup(IFileSystem files, CancellationTokenSource cancellation, bool cancel)
        : InitExecutionTargetInspector(files)
    {
        public bool Cleared { get; private set; }

        public override bool HasNonGitContent(WorkingDirectory target)
        {
            if (cancel) cancellation.Cancel();
            return base.HasNonGitContent(target);
        }

        public override void ClearExceptGit(WorkingDirectory target)
        {
            Cleared = true;
            base.ClearExceptGit(target);
        }
    }

    private static Dictionary<string, byte[]> PublicSnapshot(string project) =>
        Directory.GetFiles(project, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
}
