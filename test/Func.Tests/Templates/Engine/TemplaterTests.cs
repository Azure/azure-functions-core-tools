// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Settings;
using NSubstitute;

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
        ICoreTemplateComponent registration = Substitute.For<ICoreTemplateComponent>();
        registration.InterfaceType.Returns(typeof(ITestComponent));
        registration.Instance.Returns(component);
        using Templater templater = new(Context(), _hive, [registration]);

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

    private Templater CreateTemplater()
        => new(Context(), _hive, new FuncTemplateComponents([], [], Substitute.For<IInteractionService>()).Get());

    private interface ITestComponent : IIdentifiedComponent
    {
    }

    private sealed class TestComponent : ITestComponent, IIdentifiedComponent
    {
        public Guid Id { get; } = Guid.NewGuid();
    }
}
