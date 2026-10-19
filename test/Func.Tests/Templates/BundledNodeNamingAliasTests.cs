// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("JavaScript", "js", ".js", "--trigger-function-name")]
    [InlineData("JavaScript", "js", ".js", "--trigger-function-name=")]
    [InlineData("JavaScript", "js", ".js", "--trigger-function-name:")]
    [InlineData("JavaScript", "js", ".js", "--name")]
    [InlineData("JavaScript", "js", ".js", "-n")]
    [InlineData("TypeScript", "ts", ".ts", "--trigger-function-name")]
    [InlineData("TypeScript", "ts", ".ts", "--trigger-function-name=")]
    [InlineData("TypeScript", "ts", ".ts", "--name")]
    [InlineData("TypeScript", "ts", ".ts", "-n")]
    public async Task NameAlias_NativeTimer_PreviewAndCreateUseOneExplicitFilenameAndRegistration(
        string language, string languageAlias, string extension, string spelling)
    {
        string project = await CreateNamingAliasProjectAsync(languageAlias);
        const string name = "AliasMixed_17";
        string[] option = spelling.EndsWith('=') || spelling.EndsWith(':') ? [spelling + name] : [spelling, name];
        string[] arguments = ["new", project, "-t", "TimerTrigger-" + language, .. option, "--non-interactive", "--output=json"];
        string[] original = [.. arguments];
        var before = PublicStateSnapshot();
        TestInteractionService preview = new();

        int previewExit = await InvokePublicAsync([.. arguments, "--dry-run"], preview);

        previewExit.Should().Be(0, preview.AllOutput);
        PublicStateSnapshot().Should().Equal(before);
        using var planned = ReadPublicJson(preview);
        planned.RootElement.GetProperty("kind").GetString().Should().Be("previewed");
        planned.RootElement.GetProperty("invocation").GetProperty("status").GetString().Should().Be("Success");
        var plannedChanges = planned.RootElement.GetProperty("invocation").GetProperty("changes");
        plannedChanges.GetArrayLength().Should().Be(1);
        plannedChanges[0].GetProperty("path").GetString().Should().Be("src/functions/" + name + extension);

        TestInteractionService output = new();
        int exit = await InvokePublicAsync(arguments, output);

        exit.Should().Be(0, output.AllOutput);
        using var created = ReadPublicJson(output);
        created.RootElement.GetProperty("kind").GetString().Should().Be("created");
        created.RootElement.GetProperty("invocation").GetProperty("status").GetString().Should().Be("Success");
        created.RootElement.GetProperty("invocation").GetProperty("changes").GetRawText().Should().Be(plannedChanges.GetRawText());
        string target = Path.Combine(project, "src", "functions", name + extension);
        string content = File.ReadAllText(target);
        content.Should().Contain("app.timer('" + name + "'").And.Contain("schedule: '0 */5 * * * *'");
        if (languageAlias == "ts")
        {
            content.Should().Contain("export async function " + name + "(").And.Contain("handler: " + name);
        }
        content.Should().NotContain("app.timer('timerTrigger'").And.NotContain("funcNodeItem");
        var after = PublicStateSnapshot();
        after.Should().HaveCount(before.Count + 1);
        foreach (var file in before) after.Should().Contain(file.Key, file.Value);
        preview.AllOutput.Should().NotContain("INPUT:").And.NotContain("SELECT:");
        output.AllOutput.Should().NotContain("INPUT:").And.NotContain("SELECT:");
        _source.ReceivedCalls().Should().BeEmpty();
        arguments.Should().Equal(original);
    }

    [Theory]
    [InlineData("invalidArguments", "--trigger-function-name", "One", "--name", "Two")]
    [InlineData("invalidArguments", "--name", "One", "--trigger-function-name", "Two")]
    [InlineData("invalidArguments", "-n", "One", "--trigger-function-name", "Two")]
    [InlineData("invalidArguments", "--trigger-function-name", "One", "--trigger-function-name", "Two")]
    [InlineData("invalidArguments", "--trigger-function-name=One", "--trigger-function-name=One")]
    [InlineData("invalidArguments", "--name", "One", "-n", "Two")]
    [InlineData("invalidArguments", "--trigger-function-name")]
    [InlineData("invalidArguments", "--trigger-function-name=")]
    [InlineData("invalidName", "--trigger-function-name", " ")]
    [InlineData("invalidLegacyInput", "--trigger-function-name=../escape")]
    [InlineData("invalidArguments", "--trigger-function-nam=Typo")]
    public async Task NameAlias_NativeInvalidOrRepeatedInput_RefusesWithoutMutationEvenWithForce(string kind, params string[] tail)
    {
        string project = await CreateNamingAliasProjectAsync("js");
        var before = PublicStateSnapshot();
        TestInteractionService output = new();

        int exit = await InvokePublicAsync(
            ["new", project, "-t", "TimerTrigger-JavaScript", "--force", "--non-interactive", "--output=json", .. tail], output);

        exit.Should().Be(1, output.AllOutput);
        using var result = ReadPublicJson(output);
        result.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        result.RootElement.GetProperty("kind").GetString().Should().Be(kind);
        PublicStateSnapshot().Should().Equal(before);
        _source.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("JavaScript", "js", ".js")]
    [InlineData("TypeScript", "ts", ".ts")]
    public async Task NameAlias_NativeOmittedName_RetainsAuthoredDefault(string language, string alias, string extension)
    {
        string project = await CreateNamingAliasProjectAsync(alias);
        TestInteractionService output = new();

        int exit = await InvokePublicAsync(
            ["new", project, "-t", "TimerTrigger-" + language, "--non-interactive", "--output=json"], output);

        exit.Should().Be(0, output.AllOutput);
        string content = File.ReadAllText(Path.Combine(project, "src", "functions", "timerTrigger" + extension));
        content.Should().Contain("app.timer('timerTrigger'");
        _source.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task NameAlias_NativeOtherItem_UsesSharedHostNaming()
    {
        string project = await CreateNamingAliasProjectAsync("js");
        TestInteractionService output = new();

        int exit = await InvokePublicAsync(
            ["new", project, "-t", "http", "--trigger-function-name", "SharedName", "--non-interactive", "--output=json"], output);

        exit.Should().Be(0, output.AllOutput);
        File.ReadAllText(Path.Combine(project, "src", "functions", "SharedName.js")).Should().Contain("app.http('SharedName'");
        _source.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task NameAlias_NativeHelp_AdvertisesHostSpellingWithoutProjectMutation()
    {
        string project = await CreateNamingAliasProjectAsync("js");
        var before = PublicStateSnapshot();
        TestInteractionService output = new();

        int exit = await InvokePublicAsync(["new", project, "-t", "TimerTrigger-JavaScript", "--help"], output);

        exit.Should().Be(0, output.AllOutput);
        output.AllOutput.Should().Contain("--trigger-function-name").And.Contain("--name").And.Contain("--timer-trigger-schedule");
        PublicStateSnapshot().Should().Equal(before);
        _source.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task NameAlias_NativeCancellation_StopsWithoutWriting()
    {
        string project = await CreateNamingAliasProjectAsync("js");
        var before = PublicStateSnapshot();
        TestInteractionService output = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        int exit = await new NativeCliEntryPoint().ExecuteAsync(
            ["new", project, "-t", "TimerTrigger-JavaScript", "--trigger-function-name", "CancelledTimer", "--non-interactive", "--output=json"],
            output, _paths, new(Home), cancellation.Token, TrackEntryContexts, _ => Task.FromResult<string?>(null));

        exit.Should().Be(130);
        PublicStateSnapshot().Should().Equal(before);
        _source.ReceivedCalls().Should().BeEmpty();
    }

    private async Task<string> CreateNamingAliasProjectAsync(string language)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string project = Path.Combine(_root, "naming-alias-" + language);
        TestInteractionService output = new();
        int exit = await InvokePublicAsync(
            ["init", project, "-s", "node", "-l", language, "--no-bundles", "--skip-npm-install", "--non-interactive", "--output=json"], output);
        exit.Should().Be(0, output.AllOutput);
        return project;
    }
}
