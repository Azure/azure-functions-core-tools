// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Tests.Commands.Setup;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    private static string? ReviewPublishedCli => Environment.GetEnvironmentVariable("FUNC_TEST_PUBLISHED_CLI");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Program_SelectedStackOwner_IgnoresUnrelatedConfiguredRuntimeNativeAssetsInEitherRegistrationOrder(bool unrelatedFirst)
    {
        WorkloadRuntimeIdentifierProvider runtime = new();
        var installer = CreateInstaller(new FileWorkloadOperationCoordinator(_paths), runtimeOverride: runtime);
        string owner = CohortArchive("1.0.0", "selected-owner", runtime.Current);
        string unrelated = UnrelatedRuntimeArchive("2.0.0", "unrelated-runtime", runtime.Current);
        string[] order = unrelatedFirst ? [unrelated, owner] : [owner, unrelated];
        foreach (string package in order) await installer.InstallFromPackageAsync(package);
        using (var host = await CreateCohortHostAsync())
        using (var invocation = host.CreateInvocation())
        {
            var capture = invocation.Services.GetRequiredService<NativeTemplateWorkloadCapture>();
            var catalog = invocation.Services.GetRequiredService<InstalledProjectStackCatalog>();
            catalog.Stacks.Should().ContainSingle().Which.Stack.Should().Be("node");
            var selected = capture.GetStackOrigin(catalog.FindStack("node")!);
            selected!.PackageId.Should().Be("migration.stack." + runtime.Current);
            capture.GetCompletedRegistrations().Should().HaveCount(2).And.OnlyContain(value => value.Workload != null && value.Failure == null);
            var facts = await invocation.Services.GetRequiredService<INativeTemplateAvailability>().CaptureAsync(CancellationToken.None);
            var scoped = invocation.Services.GetRequiredService<NativeTemplateStackCompatibility>().ForStack(facts, "node");
            scoped.Workloads.Select(value => (value.PackageId, value.Version, value.IsUsable)).Should()
                .BeEquivalentTo(facts.Workloads.Select(value => (value.PackageId, value.Version, value.IsUsable)));
        }
        string project = Path.Combine(_root, "selected-owner-project");
        Directory.CreateDirectory(project);
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(1));

        var initialized = await SetupProgramAdvisoryTests.RunCliAsync(Path.Combine(_root, "owner-process"), Home, _paths.Home, project,
            ["init", project, "-s", "node", "-l", "js", "--no-bundles", "--skip-npm-install"], timeout.Token, ReviewPublishedCli);

        initialized.ExitCode.Should().Be(0, initialized.StandardOutput + initialized.StandardError);
        using var packageJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "package.json")));
        packageJson.RootElement.GetProperty("funcTemplateCohort").GetString().Should().Be("selected-owner",
            "only the physical workload that contributed the selected IProjectStack may supply its template asset cohort");
        var created = await SetupProgramAdvisoryTests.RunCliAsync(Path.Combine(_root, "owner-process"), Home, _paths.Home, project,
            ["new", project, "-t", "HttpTrigger-JavaScript", "-n", "SelectedOwner", "--output=json"], timeout.Token, ReviewPublishedCli);
        created.ExitCode.Should().Be(0, created.StandardOutput + created.StandardError);
        File.ReadAllText(Path.Combine(project, "src", "functions", "SelectedOwner.js"))
            .Should().Contain("// cohort:selected-owner").And.NotContain("// cohort:unrelated-runtime");
    }

    [Theory]
    [InlineData(".func")]
    [InlineData("src")]
    [InlineData("src/functions")]
    public async Task Program_Force_ClearsRegularGeneratedDirectoryAncestorsOnlyAfterQualification(string relative)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string target = Path.Combine(_root, "force-file-ancestor");
        string collision = Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(collision)!);
        File.WriteAllText(collision, "explicitly force-authorized regular user file");
        Directory.CreateDirectory(Path.Combine(target, ".git"));
        File.WriteAllText(Path.Combine(target, ".git", "HEAD"), "ref: refs/heads/preserved");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(1));

        var result = await SetupProgramAdvisoryTests.RunCliAsync(Path.Combine(_root, "force-process"), Home, _paths.Home, target,
            ["init", target, "-s", "node", "-l", "js", "--force", "--no-bundles", "--skip-npm-install"], timeout.Token, ReviewPublishedCli);

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        File.ReadAllText(Path.Combine(target, ".git", "HEAD")).Should().Be("ref: refs/heads/preserved");
        Directory.Exists(Path.Combine(target, "src", "functions")).Should().BeTrue();
        File.Exists(Path.Combine(target, ".func", "config.json")).Should().BeTrue();
        File.Exists(Path.Combine(target, "package.json")).Should().BeTrue();
    }

    [Theory]
    [InlineData("directory", 0)]
    [InlineData("withoutForce", 1)]
    public async Task Program_ForceAncestorControls_PreserveNormalCreationAndNonforcedUserContent(string scenario, int expected)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string target = Path.Combine(_root, "force-ancestor-control");
        Directory.CreateDirectory(target);
        string user = Path.Combine(target, ".func");
        if (scenario == "directory") Directory.CreateDirectory(user);
        else File.WriteAllText(user, "must remain without force");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(1));
        string[] force = scenario == "directory" ? ["--force"] : [];

        var result = await SetupProgramAdvisoryTests.RunCliAsync(Path.Combine(_root, "control-process"), Home, _paths.Home, target,
            ["init", target, "-s", "node", "-l", "js", "--no-bundles", "--skip-npm-install", .. force], timeout.Token, ReviewPublishedCli);

        result.ExitCode.Should().Be(expected, result.StandardOutput + result.StandardError);
        if (scenario == "withoutForce")
        {
            File.ReadAllText(user).Should().Be("must remain without force");
            File.Exists(Path.Combine(target, "package.json")).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("start", "new")]
    [InlineData("start", "init")]
    [InlineData("workload", "install", "new")]
    [InlineData("setup", "--source", "new")]
    [InlineData("--verbose=false", "run", "new", "--port", "7331")]
    [InlineData("workload", "install", "--source", "new", "ordinary")]
    [InlineData("workload", "install", "--", "new")]
    [InlineData("start", "--script-root", "new", "--help")]
    [InlineData("--", "new")]
    public async Task TemplatePreclassification_UnrelatedCommandArguments_AreNotTemplateInvocations(params string[] arguments)
    {
        using var host = await CreateCohortHostAsync();
        using var invocation = host.CreateCommandScope();
        var root = Parser.CreateCommand(invocation.Services, new NativeEntryCommandBinding(invocation.Services, arguments));
        var parsed = NativeCliEntryPoint.ParseArguments(root, arguments);
        bool result = NativeCliEntryPoint.IsTemplateInvocation(parsed);

        result.Should().BeFalse("the selected command, not a later argument named init/new, owns the host configuration policy");
    }

    [Theory]
    [InlineData("init", "target")]
    [InlineData("--verbose=true", "new", "target", "-t", "http", "--output=json")]
    [InlineData("help", "new")]
    [InlineData("new", "--", "init")]
    public async Task TemplateClassification_ActualTemplateCommandsAndHelp_RetainNativeInputPolicy(params string[] arguments)
    {
        using var host = await CreateCohortHostAsync();
        using var invocation = host.CreateCommandScope();
        var root = Parser.CreateCommand(invocation.Services, new NativeEntryCommandBinding(invocation.Services, arguments));
        var parsed = NativeCliEntryPoint.ParseArguments(root, arguments);

        NativeCliEntryPoint.IsTemplateInvocation(parsed).Should().BeTrue();
    }

    [Azure.Functions.Cli.Tests.Templates.Engine.SymbolicLinkFact]
    public async Task Program_ForcePreview_StillRefusesLinkedGeneratedAncestorsWithoutChangingOutsideContent()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(1));
        foreach (string ancestor in new[] { ".func", "src" })
        {
            string target = Path.Combine(_root, "linked-" + ancestor);
            string outside = Path.Combine(_root, "outside-" + ancestor);
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "sentinel.txt"), "never modify through a generated-path link");
            Directory.CreateSymbolicLink(Path.Combine(target, ancestor), outside);
            var before = MigrationFiles(outside);

            var result = await SetupProgramAdvisoryTests.RunCliAsync(Path.Combine(_root, "link-process"), Home, _paths.Home, target,
                ["init", target, "-s", "node", "-l", "js", "--force", "--no-bundles", "--skip-npm-install"], timeout.Token, ReviewPublishedCli);

            result.ExitCode.Should().Be(1, result.StandardOutput + result.StandardError);
            string.Join(' ', result.StandardError.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Should()
                .Contain("symbolic link or reparse point");
            AssertMigrationFilesUnchanged(outside, before);
            File.Exists(Path.Combine(target, "package.json")).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("declined")]
    [InlineData("cancelAtConfirmation")]
    [InlineData("cancelBeforeCleanup")]
    public async Task NativeForceFutureView_DoesNotClearRegularAncestorsBeforeConfirmationOrCancellation(string scenario)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string target = Path.Combine(_root, "future-view-cancel");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, ".func"), "user regular ancestor");
        File.WriteAllText(Path.Combine(target, "src"), "another regular ancestor");
        var before = MigrationFiles(target);
        using CancellationTokenSource cancellation = new();
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(true);
        interaction.ConfirmAsync("Continue?", false, true, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (scenario == "cancelAtConfirmation") cancellation.Cancel();
            return Task.FromResult(scenario != "declined");
        });
        var targets = new CancelBeforePublicCleanup(_files, cancellation, scenario == "cancelBeforeCleanup");

        int exit = await new NativeCliEntryPoint().ExecuteAsync(
            ["init", target, "-s", "node", "-l", "js", "--force", "--no-bundles", "--skip-npm-install"],
            interaction, _paths, new(Home), cancellation.Token, services =>
            {
                TrackEntryContexts(services);
                services.AddSingleton<InitExecutionTargetInspector>(targets);
            }, _ => Task.FromResult<string?>(null));

        exit.Should().Be(scenario == "declined" ? 1 : 130);
        AssertMigrationFilesUnchanged(target, before);
        targets.Cleared.Should().BeFalse();
        await interaction.Received(1).ConfirmAsync("Continue?", false, true, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("ordinary", false)]
    [InlineData("new", false)]
    [InlineData("init", false)]
    [InlineData("new", true)]
    [InlineData("init", true)]
    public async Task Program_SdkDiCommand_CapturesProjectConfigurationBeforeConstructionAndSupportsLiteralValuesAndResponses(string literal, bool response)
    {
        WorkloadRuntimeIdentifierProvider runtime = new();
        var installer = CreateInstaller(new FileWorkloadOperationCoordinator(_paths), runtimeOverride: runtime);
        await installer.InstallFromPackageAsync(UnrelatedRuntimeArchive("1.0.0", "probe", runtime.Current, "ConfigurationProbeWorkload"));
        Directory.CreateDirectory(Home);
        File.WriteAllText(Path.Combine(Home, CliConfigurationNames.ConfigFileName), """{"review":{"value":"user","named":"user","direct":"user"}}""");
        string project = Path.Combine(_root, "configuration-project");
        Directory.CreateDirectory(Path.Combine(project, ".func"));
        File.WriteAllText(Path.Combine(project, ".func", "config.json"), """{"review":{"value":"project","named":"project","direct":"project"}}""");
        string[] arguments = ["configuration-probe", literal, "--value", literal];
        if (response)
        {
            string file = Path.Combine(_root, "probe.rsp");
            File.WriteAllText(file, $"configuration-probe {literal} --value {literal}");
            arguments = ["@" + file];
        }
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(1));

        var result = await SetupProgramAdvisoryTests.RunCliAsync(Path.Combine(_root, "probe-process"), Home, _paths.Home, project,
            arguments, timeout.Token, ReviewPublishedCli);

        result.ExitCode.Should().Be(73, result.StandardOutput + result.StandardError);
        result.StandardError.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("new")]
    [InlineData("init")]
    public async Task Program_UnrelatedAliasWithTemplateNamedPath_PreservesOriginalCwdConfigurationFailure(string path)
    {
        string project = Path.Combine(_root, "bad-cwd");
        Directory.CreateDirectory(Path.Combine(project, ".func"));
        File.WriteAllText(Path.Combine(project, ".func", "config.json"), "{malformed project configuration");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(1));

        var result = await SetupProgramAdvisoryTests.RunCliAsync(Path.Combine(_root, "bad-cwd-process"), Home, _paths.Home, project,
            ["start", path, "--help"], timeout.Token, ReviewPublishedCli);

        result.ExitCode.Should().Be(1, result.StandardOutput + result.StandardError);
        string.Join(' ', result.StandardError.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Should().Contain("Failed to load configuration");
    }

    [Fact]
    public async Task Program_ValidAmbientCwdProject_CannotOverrideDifferentExplicitNativeTarget()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string cwd = Path.Combine(_root, "ambient-cwd");
        Directory.CreateDirectory(Path.Combine(cwd, ".func"));
        File.WriteAllText(Path.Combine(cwd, ".func", "config.json"),
            """{"stack":{"runtime":"dotnet","language":"F#"},"profiles":["ambient-must-not-resolve"],"defaultProfile":"ambient-must-not-resolve"}""");
        string target = Path.Combine(_root, "explicit-js-target");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(1));
        string[] init = ["init", target, "-s", "node", "-l", "js", "--no-bundles", "--skip-npm-install"];
        var created = await SetupProgramAdvisoryTests.RunCliAsync(Path.Combine(_root, "ambient-process"), Home, _paths.Home, cwd,
            init, timeout.Token, ReviewPublishedCli);
        created.ExitCode.Should().Be(0, created.StandardOutput + created.StandardError);
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, ".func", "config.json")));
        config.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
        config.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be("JavaScript");

        var item = await SetupProgramAdvisoryTests.RunCliAsync(Path.Combine(_root, "ambient-process"), Home, _paths.Home, cwd,
            ["new", target, "-t", "HttpTrigger-JavaScript", "-n", "ExplicitTarget", "--output=json"], timeout.Token, ReviewPublishedCli);

        item.ExitCode.Should().Be(0, item.StandardOutput + item.StandardError);
        item.StandardError.Should().BeEmpty();
        File.ReadAllText(Path.Combine(target, "src", "functions", "ExplicitTarget.js")).Should().Contain("app.http('ExplicitTarget'");
        Directory.GetFiles(cwd, "*", SearchOption.AllDirectories).Should().ContainSingle();
    }

    private string UnrelatedRuntimeArchive(string version, string marker, string rid, string workloadType = "StubWorkload")
    {
        string path = CohortArchive(version, marker, rid);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        MigrationChangeVersion(zip, version, "Bound.Unrelated.Runtime." + rid);
        var nuspec = zip.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        XDocument package;
        using (Stream input = nuspec.Open()) package = XDocument.Load(input);
        XNamespace ns = package.Root!.Name.Namespace;
        XElement tags = package.Root.Element(ns + "metadata")!.Element(ns + "tags")!;
        tags.Value = tags.Value.Replace("alias:node", "alias:unrelated-runtime", StringComparison.OrdinalIgnoreCase);
        string nuspecPath = nuspec.FullName;
        nuspec.Delete();
        using (Stream output = zip.CreateEntry(nuspecPath).Open()) package.Save(output);
        var manifest = zip.GetEntry("workload.json")!;
        JsonNode metadata;
        using (StreamReader reader = new(manifest.Open())) metadata = JsonNode.Parse(reader.ReadToEnd())!;
        const string fixture = "Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand.dll";
        metadata["entryPoint"] = new JsonObject
        {
            ["assemblyPath"] = "tools/any/" + fixture,
            ["type"] = "Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand." + workloadType,
        };
        manifest.Delete();
        using (StreamWriter writer = new(zip.CreateEntry("workload.json").Open())) writer.Write(metadata.ToJsonString());
        using (Stream source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "tools", "any", fixture)))
        using (Stream output = zip.CreateEntry("tools/any/" + fixture).Open()) source.CopyTo(output);
        return path;
    }
}
