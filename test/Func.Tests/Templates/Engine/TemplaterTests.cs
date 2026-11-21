// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplaterTests : IDisposable
{
    private readonly string _root;
    private readonly string _hive;

    public TemplaterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "func-templater-tests-" + Guid.NewGuid().ToString("N"));
        _hive = Path.Combine(_root, "hive");
        Directory.CreateDirectory(_hive);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup of the temp hive
        }
    }

    [Fact]
    public async Task ListAsync_ReturnsInstalledTemplates()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        TemplateResolutionResult result = await templater.ListAsync(CancellationToken.None);

        result.Templates.Select(template => template.Identity).Should().BeEquivalentTo(
        [
            TemplateFixtures.ItemTemplateIdentity,
            TemplateFixtures.ProjectTemplateIdentity,
            TemplateFixtures.OtherTypeTemplateIdentity,
            TemplateFixtures.UntypedTemplateIdentity,
        ]);
        result.Templates.Should().AllSatisfy(template => template.Template.Identity.Should().Be(template.Identity));
    }

    [Fact]
    public async Task ListAsync_LoadsFuncHostDataOnDiscoveryAndCacheReload()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using Templater templater = CreateTemplater();
            TemplateResolutionResult result = await templater.ListAsync(CancellationToken.None);

            FuncTemplateInfo item = result.Templates.Single(template => template.Identity == TemplateFixtures.ItemTemplateIdentity);
            item.HostConfigPlace.Should().EndWith("func.host.json");
            item.Template.Should().BeAssignableTo<ITemplateInfoHostJsonCache>()
                .Which.HostData.Should().NotBeNullOrWhiteSpace();
            item.HostData.SymbolInfo["AuthorizationLevel"].LongName.Should().Be("auth-level");
            item.HostData.SymbolInfo["AuthorizationLevel"].ShortName.Should().Be("a");
            item.HostData.UsageExamples.Should().Equal("func new --auth-level anonymous");
            item.HostData.IsHidden.Should().BeFalse();
            result.Templates.Single(template => template.Identity == TemplateFixtures.ProjectTemplateIdentity)
                .HostData.SymbolInfo.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ListAsync_InvalidFuncHostFile_ThrowsInsteadOfUsingEmptyMetadata()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root, itemHostJson: "{");
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        Func<Task> act = () => templater.ListAsync(CancellationToken.None);

        await act.Should().ThrowAsync<JsonException>().WithMessage($"*{TemplateFixtures.ItemTemplateIdentity}*func.host.json*");
    }

    [Fact]
    public async Task ListAsync_TemplateWithFuncHostJson_BindsDeclaredOptions()
    {
        string packageDirectory = TemplateFixtures.WriteParameterPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();
        TemplateArgumentsArgument templateArguments = new();
        Command command = new("init") { Arguments = { new PathArgument(releaseOptionTokens: true), templateArguments } };
        ParseResult parseResult = command.Parse(["myproj", "-f", "net8.0", "--no-http"]);

        TemplateResolutionResult result = await templater.ListAsync(CancellationToken.None);
        FuncTemplateInfo template = result.Templates.Should().ContainSingle().Subject;
        IReadOnlyDictionary<string, string> values = new TemplateParameterBinder().Bind(template, parseResult, templateArguments.GetTokens(parseResult), []);

        values.Should().BeEquivalentTo(new Dictionary<string, string> { ["Framework"] = "net8.0", ["NoHttp"] = "true" });
    }

    [Fact]
    public async Task FindProjectTemplateAsync_MatchingLanguage_ReturnsOnlyThatProjectTemplate()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        await TemplateFixtures.InstallAsync(CsharpProjectPackage("csharp", "Func.Tests.CSharp"), _hive);
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteProjectPackage(Path.Combine(_root, "fsharp"), "Func.Tests.FSharp", "F#"), _hive);
        using Templater templater = CreateTemplater();

        FuncTemplateInfo? template = await templater.FindProjectTemplateAsync("c#");

        template.Should().NotBeNull();
        template.Identity.Should().Be("Func.Tests.CSharp");
    }

    [Fact]
    public async Task FindProjectTemplateAsync_NoLanguageMatch_ReturnsNull()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        await TemplateFixtures.InstallAsync(CsharpProjectPackage("csharp", "Func.Tests.CSharp"), _hive);
        using Templater templater = CreateTemplater();

        FuncTemplateInfo? template = await templater.FindProjectTemplateAsync("python");

        template.Should().BeNull();
    }

    [Fact]
    public async Task FindProjectTemplateAsync_MultipleMatches_Throws()
    {
        await TemplateFixtures.InstallAsync(CsharpProjectPackage("first", "Func.Tests.First"), _hive);
        await TemplateFixtures.InstallAsync(CsharpProjectPackage("second", "Func.Tests.Second"), _hive);
        using Templater templater = CreateTemplater();

        Func<Task> act = () => templater.FindProjectTemplateAsync("C#");

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Contain("Func.Tests.First").And.Contain("Func.Tests.Second");
    }

    [Fact]
    public async Task FindItemTemplatesAsync_ReturnsOnlyItemTemplatesForLanguage()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteItemPackage(Path.Combine(_root, "csharp"), "Func.Tests.CSharpItem", "Http", "C#"), _hive);
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteItemPackage(Path.Combine(_root, "fsharp"), "Func.Tests.FSharpItem", "Http", "F#"), _hive);
        await TemplateFixtures.InstallAsync(CsharpProjectPackage("project", "Func.Tests.CSharp"), _hive);
        using Templater templater = CreateTemplater();

        IReadOnlyList<ITemplateInfo> templates = await templater.FindItemTemplatesAsync("c#");

        templates.Select(t => t.Identity).Should().Equal("Func.Tests.CSharpItem");
    }

    [Theory]
    [InlineData("Func.Tests.CSharpItem", 1)]
    [InlineData("FUNC-TESTS-ITEM", 1)]
    [InlineData("http", 1)]
    [InlineData("timer", 0)]
    public async Task FindItemTemplatesAsync_Reference_MatchesIdentityShortNameOrName(string reference, int expectedCount)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteItemPackage(_root, "Func.Tests.CSharpItem", "Http", "C#"), _hive);
        using Templater templater = CreateTemplater();

        IReadOnlyList<ITemplateInfo> templates = await templater.FindItemTemplatesAsync("C#", reference);

        templates.Should().HaveCount(expectedCount);
    }

    [Fact]
    public async Task FindItemTemplatesAsync_ExactIdentityOutranksAnotherTemplatesName()
    {
        await TemplateFixtures.InstallAsync(
            TemplateFixtures.WriteItemPackage(Path.Combine(_root, "target"), "Func.Tests.Http", "Target", "C#"), _hive);
        await TemplateFixtures.InstallAsync(
            TemplateFixtures.WriteItemPackage(Path.Combine(_root, "other"), "Func.Tests.Other", "Func.Tests.Http", "C#"), _hive);
        using Templater templater = CreateTemplater();

        IReadOnlyList<ITemplateInfo> byIdentity = await templater.FindItemTemplatesAsync("C#", "Func.Tests.Http");
        IReadOnlyList<ITemplateInfo> bySharedShortName = await templater.FindItemTemplatesAsync("C#", "func-tests-item");

        byIdentity.Select(t => t.Identity).Should().Equal("Func.Tests.Http");
        bySharedShortName.Should().HaveCount(2);
    }

    [Fact]
    public async Task InstantiateAsync_WritesProjectUsingNameAndParameters()
    {
        using Templater templater = await CreateCsharpProjectTemplaterAsync();
        FuncTemplateInfo template = (await templater.FindProjectTemplateAsync("C#"))!;
        string output = Path.Combine(_root, "out");

        ITemplateCreationResult result = await templater.InstantiateAsync(
            template, "MyApp", output, new Dictionary<string, string> { ["Framework"] = "net11.0" }, force: false);

        result.Status.Should().Be(CreationResultStatus.Success);
        File.ReadAllText(Path.Combine(output, "MyApp.csproj")).Should().Contain("net11.0");
    }

    [Fact]
    public async Task InstantiateAsync_NoName_UsesOutputDirectoryName()
    {
        using Templater templater = await CreateCsharpProjectTemplaterAsync();
        FuncTemplateInfo template = (await templater.FindProjectTemplateAsync("C#"))!;
        string output = Path.Combine(_root, "sample-app");

        ITemplateCreationResult result = await templater.InstantiateAsync(
            template, name: null, output, new Dictionary<string, string>(), force: false);

        result.Status.Should().Be(CreationResultStatus.Success);
        File.ReadAllText(Path.Combine(output, "sample-app.csproj")).Should().Contain("net10.0");
    }

    [Fact]
    public async Task InstantiateAsync_InvalidParameterValue_ReturnsInvalidParamValues()
    {
        using Templater templater = await CreateCsharpProjectTemplaterAsync();
        FuncTemplateInfo template = (await templater.FindProjectTemplateAsync("C#"))!;
        string output = Path.Combine(_root, "out");

        ITemplateCreationResult result = await templater.InstantiateAsync(
            template, "MyApp", output, new Dictionary<string, string> { ["Framework"] = "net6.0" }, force: false);

        result.Status.Should().Be(CreationResultStatus.InvalidParamValues);
        File.Exists(Path.Combine(output, "MyApp.csproj")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, CreationResultStatus.DestructiveChangesDetected, "existing")]
    [InlineData(true, CreationResultStatus.Success, "<TargetFramework>net10.0</TargetFramework>")]
    public async Task InstantiateAsync_ExistingFile_OverwritesOnlyWhenForced(bool force, CreationResultStatus expectedStatus, string expectedContent)
    {
        using Templater templater = await CreateCsharpProjectTemplaterAsync();
        FuncTemplateInfo template = (await templater.FindProjectTemplateAsync("C#"))!;
        string output = Path.Combine(_root, "out");
        Directory.CreateDirectory(output);
        string projectFile = Path.Combine(output, "MyApp.csproj");
        File.WriteAllText(projectFile, "existing");

        ITemplateCreationResult result = await templater.InstantiateAsync(
            template, "MyApp", output, new Dictionary<string, string>(), force);

        result.Status.Should().Be(expectedStatus);
        File.ReadAllText(projectFile).Should().Be(expectedContent);
    }

    [Fact]
    public async Task FindProjectTemplateAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        Templater templater = CreateTemplater();
        templater.Dispose();

        Func<Task> act = () => templater.FindProjectTemplateAsync("C#");

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task ListAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        using Templater templater = CreateTemplater();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> act = () => templater.ListAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ListAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        Templater templater = CreateTemplater();
        templater.Dispose();

        Func<Task> act = () => templater.ListAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Constructor_NullContext_Throws()
    {
        Action act = () => _ = new Templater(null!, _hive, []);

        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankSettingsLocation_Throws(string settingsLocation)
    {
        Action act = () => _ = new Templater(Context(), settingsLocation, []);

        act.Should().Throw<ArgumentException>().WithParameterName("settingsLocation");
    }

    [Fact]
    public void Constructor_NullComponents_Throws()
    {
        Action act = () => _ = new Templater(Context(), _hive, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("components");
    }

    [Fact]
    public void Settings_UseTheSuppliedSettingsLocation()
    {
        using Templater templater = CreateTemplater();

        templater.Settings.Paths.GlobalSettingsDir.Should().Be(_hive);
    }

    [Fact]
    public void Settings_RegisterRunnableProjectGenerators()
    {
        using Templater templater = CreateTemplater();

        templater.Settings.Components.OfType<IGenerator>().Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_RegistersSuppliedComponents()
    {
        TestComponent component = new();
        using Templater templater = new(Context(), _hive, [new TestTemplateComponent(component)]);

        templater.Settings.Components.OfType<ITestComponent>().Should().ContainSingle().Which.Should().BeSameAs(component);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        Templater templater = CreateTemplater();
        templater.Dispose();

        Action act = templater.Dispose;

        act.Should().NotThrow();
    }

    private TemplateEngineContext Context() => new(WorkingDirectory.FromExplicit(_root));

    private string CsharpProjectPackage(string directoryName, string identity)
        => TemplateFixtures.WriteProjectPackage(Path.Combine(_root, directoryName), identity, "C#");

    private async Task<Templater> CreateCsharpProjectTemplaterAsync()
    {
        await TemplateFixtures.InstallAsync(CsharpProjectPackage("csharp", "Func.Tests.CSharp"), _hive);
        return CreateTemplater();
    }

    private Templater CreateTemplater() => new(Context(), _hive, []);

    private interface ITestComponent : IIdentifiedComponent
    {
    }

    private sealed class TestComponent : ITestComponent
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class TestTemplateComponent(IIdentifiedComponent instance) : ITemplateComponent
    {
        public Type InterfaceType => typeof(ITestComponent);

        public IIdentifiedComponent Instance { get; } = instance;
    }
}
