// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Security.Cryptography;
using Azure.Functions.Cli.Tests.Templates.Engine;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PublicEntry_InitDryRun_PreviewsWithoutCreatingOrClearingTargetOrOwnedState(bool force, bool json)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string target = Path.Combine(_root, "preview-project");
        if (force)
        {
            Directory.CreateDirectory(Path.Combine(target, ".git"));
            File.WriteAllText(Path.Combine(target, ".git", "HEAD"), "preserved git");
            Directory.CreateDirectory(Path.Combine(target, ".func"));
            File.WriteAllText(Path.Combine(target, ".func", "config.json"), "{malformed user configuration");
            File.WriteAllText(Path.Combine(target, "authored.txt"), "preserved user file");
        }
        var before = PublicStateSnapshot();
        string[] arguments = ["init", target, "-s", "node", "-l", "js", "--dry-run", "--non-interactive"];
        if (force) arguments = [.. arguments, "--force"];
        if (json) arguments = [.. arguments, "--output=json"];
        var output = new TestInteractionService();

        int exit = await InvokePublicAsync(arguments, output);

        exit.Should().Be(0, output.AllOutput);
        if (json)
        {
            using var result = ReadPublicJson(output);
            result.RootElement.GetProperty("kind").GetString().Should().Be("previewed");
            var invocation = result.RootElement.GetProperty("invocation");
            invocation.GetProperty("completedChanges").GetArrayLength().Should().Be(0);
            invocation.GetProperty("plannedConfigurations").GetArrayLength().Should().Be(1);
            if (force)
            {
                invocation.GetProperty("plannedCleanup").EnumerateArray().Select(path => path.GetString()).Should()
                    .BeEquivalentTo(".func", "authored.txt");
            }
            invocation.GetProperty("preparation").GetProperty("planned").EnumerateArray()
                .Should().Contain(operation => operation.GetProperty("operation").GetString() == "NodeNpmInstall"
                    && !operation.GetProperty("skipped").GetBoolean());
        }
        else
        {
            output.AllOutput.Should().Contain("no files written").And.Contain("NodeNpmInstall: Planned");
            if (force) output.AllOutput.Should().Contain("Delete: authored.txt").And.Contain("Delete: .func").And.NotContain("Delete: .git");
        }
        PublicStateSnapshot().Should().Equal(before);
        Directory.Exists(target).Should().Be(force);
        _source.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicEntry_ExistingProjectDryRun_DoesNotAdoptOrHealConfiguration(bool heal)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string target = Path.Combine(_root, "existing-preview");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "host.json"), """{"version":"2.0"}""");
        File.WriteAllText(Path.Combine(target, "package.json"), """{"main":"src/functions/*.js"}""");
        File.WriteAllText(Path.Combine(target, "local.settings.json"), """{"Values":{"FUNCTIONS_WORKER_RUNTIME":"node"}}""");
        if (heal)
        {
            Directory.CreateDirectory(Path.Combine(target, ".func"));
            File.WriteAllText(Path.Combine(target, ".func", "config.json"), """{"stack":{"runtime":"node"},"custom":42}""");
        }
        var before = PublicStateSnapshot();
        var output = new TestInteractionService();

        int exit = await InvokePublicAsync(["init", target, "--dry-run", "--output=json"], output);

        exit.Should().Be(0, output.AllOutput);
        using var result = ReadPublicJson(output);
        result.RootElement.GetProperty("kind").GetString().Should().Be(heal ? "repairPreviewed" : "adoptionPreviewed");
        result.RootElement.GetProperty("configuration").GetProperty("written").GetBoolean().Should().BeFalse();
        PublicStateSnapshot().Should().Equal(before);
        _source.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PublicEntry_NewDryRun_ReportsNativePlacementWithoutWrites(bool json, bool force)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = await CreatePublicPreviewProjectAsync();
        string target = Path.Combine(project, "nested");
        if (force)
        {
            Directory.CreateDirectory(Path.Combine(target, "src", "functions"));
            File.WriteAllText(Path.Combine(target, "src", "functions", "Greeting.js"), "preserve conflicting file");
        }
        var before = PublicStateSnapshot();
        var output = new TestInteractionService();
        string[] arguments = ["new", target, "-t", "http", "-n", "Greeting", "--authlevel=anonymous", "--dry-run", "--non-interactive"];
        if (json) arguments = [.. arguments, "--output=json"];
        if (force) arguments = [.. arguments, "--force"];

        int exit = await InvokePublicAsync(arguments, output);

        exit.Should().Be(0, output.AllOutput);
        if (json)
        {
            using var result = ReadPublicJson(output);
            result.RootElement.GetProperty("kind").GetString().Should().Be("previewed");
            result.RootElement.GetProperty("invocation").GetProperty("changes").EnumerateArray().Should()
                .Contain(change => change.GetProperty("path").GetString()!.EndsWith("Greeting.js", StringComparison.Ordinal));
        }
        else output.AllOutput.Should().Contain("no files written").And.Contain("Greeting.js");
        PublicStateSnapshot().Should().Equal(before);
        Directory.Exists(target).Should().Be(force);
        _source.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("HTTP", true)]
    [InlineData("httptrigger", true)]
    [InlineData("no-such-function-item", false)]
    public async Task PublicEntry_NewListSearch_FiltersEligibleInstalledMetadataWithoutWrites(string search, bool found)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = await CreatePublicPreviewProjectAsync();
        var before = PublicStateSnapshot();
        var output = new TestInteractionService();

        int exit = await InvokePublicAsync(["new", project, "--list", "--search", search, "--output=json"], output);

        exit.Should().Be(0, output.AllOutput);
        using var result = ReadPublicJson(output);
        result.RootElement.GetProperty("kind").GetString().Should().Be("listed");
        var templates = result.RootElement.GetProperty("templates").EnumerateArray().ToArray();
        if (found) templates.Should().Contain(template => template.GetProperty("identity").GetString() == "Azure.Functions.Node.Http.JavaScript");
        else templates.Should().BeEmpty();
        templates.Should().OnlyContain(template => template.GetProperty("language").GetString() == "JavaScript"
            && template.GetProperty("constraintStatus").GetString() == "Eligible");
        PublicStateSnapshot().Should().Equal(before);
    }

    [Theory]
    [InlineData("--authlevel")]
    [InlineData("--authlevel=")]
    [InlineData("--authlevel=invalid")]
    [InlineData("--authlevel=anonymous", "--authlevel=function")]
    [InlineData("--unknown=value")]
    [InlineData("--authlevel", "--literal")]
    public async Task PublicEntry_NewPreviewInvalidParameters_ReturnsOneJsonFailureWithoutWrites(params string[] tail)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = await CreatePublicPreviewProjectAsync();
        var before = PublicStateSnapshot();
        var output = new TestInteractionService();

        int exit = await InvokePublicAsync(["new", Path.Combine(project, "nested"), "-t", "http", "--dry-run", "--output=json", .. tail], output);

        exit.Should().Be(1, output.AllOutput);
        using var result = ReadPublicJson(output);
        result.RootElement.GetProperty("kind").GetString().Should().Be("invalidArguments");
        result.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        result.RootElement.GetProperty("argumentResults")[0].GetProperty("invalidInputDiagnostics").GetArrayLength().Should().BePositive();
        PublicStateSnapshot().Should().Equal(before);
    }

    [Theory]
    [InlineData("--search", "http")]
    [InlineData("--list", "--search=")]
    [InlineData("--list", "--search", " ")]
    [InlineData("--list", "--dry-run")]
    [InlineData("--list", "--name", "Ignored")]
    [InlineData("--list", "--force")]
    public async Task PublicEntry_InvalidDiscoveryCombinations_FailBeforeAnyWrites(params string[] tail)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = await CreatePublicPreviewProjectAsync();
        var before = PublicStateSnapshot();
        var output = new TestInteractionService();

        int exit = await InvokePublicAsync(["new", project, "--output=json", .. tail], output);

        exit.Should().Be(1, output.AllOutput);
        using var result = ReadPublicJson(output);
        result.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        PublicStateSnapshot().Should().Equal(before);
    }

    [Theory]
    [InlineData("init")]
    [InlineData("new")]
    public async Task PublicEntry_PreviewRegularFileAncestor_FailsWithoutWrites(string command)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = await CreatePublicPreviewProjectAsync();
        string file = Path.Combine(project, "regular-file");
        File.WriteAllText(file, "not a directory");
        var before = PublicStateSnapshot();
        var output = new TestInteractionService();
        string[] selection = command == "init" ? ["-s", "node", "-l", "js", "--force"] : ["-t", "http"];

        int exit = await InvokePublicAsync([command, Path.Combine(file, "child"), "--dry-run", "--output=json", .. selection], output);

        exit.Should().Be(1, output.AllOutput);
        using var result = ReadPublicJson(output);
        result.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        PublicStateSnapshot().Should().Equal(before);
    }

    [SymbolicLinkFact]
    public async Task PublicEntry_ForcePreviewLinkedRoot_PreservesResolvedTargetAndGitContent()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string outside = Path.Combine(_root, "external");
        Directory.CreateDirectory(Path.Combine(outside, ".git"));
        File.WriteAllText(Path.Combine(outside, ".git", "HEAD"), "preserve git");
        File.WriteAllText(Path.Combine(outside, "user.txt"), "preserve external");
        string link = Path.Combine(_root, "linked-target");
        Directory.CreateSymbolicLink(link, outside);
        var before = PublicStateSnapshot();
        var output = new TestInteractionService();

        int exit = await InvokePublicAsync(["init", link, "-s", "node", "-l", "js", "--force", "--dry-run", "--output=json"], output);

        exit.Should().Be(0, output.AllOutput);
        using var result = ReadPublicJson(output);
        result.RootElement.GetProperty("kind").GetString().Should().Be("previewed");
        PublicStateSnapshot().Should().Equal(before);
    }

    [Fact]
    public async Task PublicEntry_SelectedHelpAndParsing_UseSameReservedAliasesWithoutBypassingOwnership()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = await CreatePublicPreviewProjectAsync();
        string package = Path.Combine(_root, "parameter-package");
        Directory.CreateDirectory(Path.Combine(package, ".template.config"));
        File.WriteAllText(Path.Combine(package, ".template.config", "template.json"), """
            {
              "identity":"Func.Tests.PublicParameters","name":"Public parameters","shortName":"public-parameters",
              "tags":{"type":"item","language":"JavaScript"},
              "symbols":{
                "Dry":{"type":"parameter","datatype":"string","defaultValue":"dry default"},
                "Search":{"type":"parameter","datatype":"string","defaultValue":"search default"},
                "Output":{"type":"parameter","datatype":"string","defaultValue":"output default"},
                "Enabled":{"type":"parameter","datatype":"bool","defaultValue":"false"},
                "Required":{"type":"parameter","datatype":"string","isRequired":true},
                "Free":{"type":"parameter","datatype":"string"},
                "Mode":{"type":"parameter","datatype":"choice","defaultValue":"safe","choices":[{"choice":"safe"},{"choice":"fast"}]}
              }
            }
            """);
        File.WriteAllText(Path.Combine(package, ".template.config", "func.host.json"), """
            {"symbolInfo":{"Dry":{"longName":"dry-run"},"Search":{"longName":"search"},"Output":{"longName":"output"}}}
            """);
        File.WriteAllText(Path.Combine(package, "parameters.txt"), "parameter fixture");
        await TemplateFixtures.InstallAsync(package, Path.Combine(Home, "templates"), CancellationToken.None);
        var before = PublicStateSnapshot();
        var help = new TestInteractionService();

        int helpExit = await InvokePublicAsync(["new", project, "-t", "public-parameters", "--help"], help);
        var output = new TestInteractionService();
        int previewExit = await InvokePublicAsync(["new", project, "-t", "public-parameters", "--dry-run", "--output=json",
            "--param:Dry=value", "--param:Search=--literal", "--param:Output=json", "--Enabled=true", "--Required=present", "--Free="], output);

        helpExit.Should().Be(0, help.AllOutput);
        help.AllOutput.Should().Contain("--param:Dry").And.Contain("--param:Search").And.Contain("--param:Output")
            .And.Contain("Explicit value (bool)").And.Contain("Required.").And.Contain("Default: safe.").And.Contain("Choices: safe, fast.");
        previewExit.Should().Be(1, output.AllOutput);
        using var result = ReadPublicJson(output);
        result.RootElement.GetProperty("kind").GetString().Should().Be("invalidLegacyInput");
        result.RootElement.GetProperty("argumentResults")[0].GetProperty("invalidInputDiagnostics").GetArrayLength().Should().Be(0);
        var values = result.RootElement.GetProperty("argumentResults")[0].GetProperty("valuesByCanonicalName");
        values.GetProperty("Dry").GetString().Should().Be("value");
        values.GetProperty("Search").GetString().Should().Be("--literal");
        values.GetProperty("Output").GetString().Should().Be("json");
        values.GetProperty("Enabled").GetString().Should().Be("true");
        values.GetProperty("Free").GetString().Should().BeEmpty();
        values.GetProperty("Mode").GetString().Should().Be("safe");
        PublicStateSnapshot().Should().Equal(before);
    }

    [Theory]
    [InlineData("--output=json")]
    [InlineData("--non-interactive")]
    public async Task PublicEntry_InitForceNonInteractive_DoesNotAttemptConfirmationInInteractiveTerminal(string option)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string target = Path.Combine(_root, "force-noninteractive");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "authored.txt"), "explicit force replaces this");
        var output = Substitute.For<Azure.Functions.Cli.Console.IInteractionService>();
        output.IsInteractive.Returns(true);

        int exit = await new NativeCliEntryPoint().ExecuteAsync(
            ["init", target, "-s", "node", "-l", "js", "--force", "--no-bundles", "--skip-npm-install", option],
            output, _paths, new(Home), CancellationToken.None, TrackEntryContexts, _ => Task.FromResult<string?>(null));

        exit.Should().Be(0);
        File.Exists(Path.Combine(target, "authored.txt")).Should().BeFalse();
        File.Exists(Path.Combine(target, ".func", "config.json")).Should().BeTrue();
        output.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name.StartsWith("Confirm", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("init", "--name=")]
    [InlineData("init", "-n=")]
    [InlineData("init", "--stack=")]
    [InlineData("init", "-s=")]
    [InlineData("init", "--language=")]
    [InlineData("init", "-l=")]
    [InlineData("init", "--dry-run=")]
    [InlineData("init", "--force=")]
    [InlineData("new", "--name=")]
    [InlineData("new", "-n=")]
    [InlineData("new", "--template=")]
    [InlineData("new", "-t=")]
    [InlineData("new", "--dry-run=")]
    [InlineData("new", "--force=")]
    public async Task PublicEntry_EmptyInlineHostScalar_DoesNotConsumeFollowingFlagOrWrite(string command, string empty)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = await CreatePublicPreviewProjectAsync();
        string target = command == "init" ? Path.Combine(_root, "empty-name-target") : project;
        string[] selection = empty switch
        {
            "--stack=" or "-s=" => ["-l", "js"],
            "--language=" or "-l=" => ["-s", "node"],
            "--template=" or "-t=" => [],
            _ => command == "init" ? ["-s", "node", "-l", "js"] : ["-t", "http"],
        };
        string[] following = command == "init" ? ["--no-bundles", "--skip-npm-install"] : ["--force"];
        var before = PublicStateSnapshot();
        var output = new TestInteractionService();

        int exit = await InvokePublicAsync([command, target, .. selection, "--output=json", empty, .. following], output);

        exit.Should().Be(1, output.AllOutput);
        using var result = ReadPublicJson(output);
        result.RootElement.GetProperty("kind").GetString().Should().Be("invalidArguments");
        result.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        PublicStateSnapshot().Should().Equal(before);
    }

    [Theory]
    [InlineData("init", "present")]
    [InlineData("new", "present")]
    [InlineData("list", "present")]
    [InlineData("help", "present")]
    [InlineData("init", "corrupt")]
    [InlineData("new", "corrupt")]
    [InlineData("list", "corrupt")]
    [InlineData("help", "corrupt")]
    public async Task PublicEntry_PreviewPendingStoreRecovery_RefusesWithoutRestoringHiveOrRemovingJournal(string command, string state)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = await CreatePublicPreviewProjectAsync();
        string control = Path.Combine(Home, ".template-store");
        string backup = Path.Combine(control, "recovery");
        if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
        _files.CopyDirectory(Path.Combine(Home, "templates"), backup);
        File.WriteAllText(Path.Combine(Home, "templates", "live-only.txt"), "do not restore during preview");
        File.WriteAllText(Path.Combine(control, "recovery.ready"), state);
        var before = PublicStateSnapshot();
        var output = new TestInteractionService();
        string target = command == "init" ? Path.Combine(_root, "recovery-preview") : project;
        string[] selection = command switch
        {
            "init" => ["-s", "node", "-l", "js"],
            "list" => ["--list"],
            "help" => ["-t", "http", "--help"],
            _ => ["-t", "http"],
        };
        string[] mode = command == "list" ? [] : ["--dry-run"];

        int exit = await InvokePublicAsync([command is "list" or "help" ? "new" : command, target, .. selection, .. mode, "--output=json"], output);

        exit.Should().Be(1, output.AllOutput);
        if (command == "help") output.AllOutput.Should().Contain("requires recovery");
        else
        {
            using var result = ReadPublicJson(output);
            result.RootElement.GetProperty("kind").GetString().Should().Be("templateStoreRecoveryRequired");
        }
        PublicStateSnapshot().Should().Equal(before);
        _source.ReceivedCalls().Should().BeEmpty();
    }

    private async Task<string> CreatePublicPreviewProjectAsync()
    {
        string project = Path.Combine(_root, "preview-parent");
        var output = new TestInteractionService();
        int exit = await InvokePublicAsync(["init", project, "-s", "node", "-l", "js", "--no-bundles", "--skip-npm-install"], output);
        exit.Should().Be(0, output.AllOutput);
        return project;
    }

    private Task<int> InvokePublicAsync(string[] arguments, TestInteractionService output) =>
        new NativeCliEntryPoint().ExecuteAsync(arguments, output, _paths, new(Home), CancellationToken.None,
            services => TrackEntryContexts(services), _ => Task.FromResult<string?>(null));

    private Dictionary<string, string> PublicStateSnapshot() =>
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(path => path,
            path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));

    private static JsonDocument ReadPublicJson(TestInteractionService output) =>
        JsonDocument.Parse(output.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
}
