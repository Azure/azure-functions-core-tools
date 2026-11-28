// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplaterFactoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-templater-factory-tests", Guid.NewGuid().ToString("N"));

    private string Home => Path.Combine(_root, "home");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup of the temp func home
        }
    }

    [Fact]
    public void Create_BindsTheSuppliedContext()
    {
        TemplaterFactory factory = new(new CliConfigurationPathsOptions(Home), Components());
        TemplateEngineContext context = Context("command");

        using Templater templater = factory.Create(context);

        templater.Context.Should().BeSameAs(context);
    }

    [Fact]
    public async Task Create_ReadsTemplatesFromTemplatesDirectoryUnderFuncHome()
    {
        await InstallFixturesUnderHomeAsync();
        TemplaterFactory factory = new(new CliConfigurationPathsOptions(Home), Components());

        using Templater templater = factory.Create(Context("command"));
        TemplateResolutionResult result = await templater.ListAsync(CancellationToken.None);

        result.Templates.Should().Contain(template => template.Identity == TemplateFixtures.ItemTemplateIdentity);
    }

    [Fact]
    public async Task Create_EachCallReturnsAnIndependentTemplater()
    {
        await InstallFixturesUnderHomeAsync();
        TemplaterFactory factory = new(new CliConfigurationPathsOptions(Home), Components());
        Templater first = factory.Create(Context("first"));
        using Templater second = factory.Create(Context("second"));

        first.Dispose();
        TemplateResolutionResult result = await second.ListAsync(CancellationToken.None);

        result.Templates.Should().Contain(template => template.Identity == TemplateFixtures.ItemTemplateIdentity);
    }

    [Fact]
    public async Task CreateIsolated_ExcludesExternalPackagesButSupportsManagedInstallation()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        var source = Substitute.For<IWorkloadTemplateSource>();
        source.GetRootsAsync(Arg.Any<CancellationToken>()).Returns(
            [new WorkloadTemplateRoot(packageDirectory, "workload.templates", "1.0.0", DateTime.UtcNow)]);
        ICoreTemplateComponent component = Substitute.For<ICoreTemplateComponent>();
        component.InterfaceType.Returns(typeof(ITemplatePackageProviderFactory));
        component.Instance.Returns(new WorkloadTemplatePackageProviderFactory(source));
        var components = new FuncTemplateComponentsProvider([component], [], Substitute.For<IInteractionService>());
        TemplaterFactory factory = new(new CliConfigurationPathsOptions(Home), components);
        using Templater normal = factory.Create(Context("command"));
        using Templater isolated = factory.CreateIsolated(Context("command"), Path.Combine(_root, "isolated"));

        (await normal.ListAsync(TestContext.Current.CancellationToken)).Templates.Should().NotBeEmpty();
        (await isolated.ListAsync(TestContext.Current.CancellationToken)).Templates.Should().BeEmpty();
        var result = await isolated.InstallAsync(new InstallRequest(packageDirectory), TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue(result.ErrorMessage ?? string.Empty);
        (await isolated.ListAsync(TestContext.Current.CancellationToken)).Templates.Should().HaveCount(4);
    }

    [Fact]
    public void Create_NullContext_Throws()
    {
        TemplaterFactory factory = new(new CliConfigurationPathsOptions(Home), Components());

        Action act = () => factory.Create(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public void CreateIsolated_InvalidArguments_Throw()
    {
        TemplaterFactory factory = new(new CliConfigurationPathsOptions(Home), Components());

        Action nullContext = () => factory.CreateIsolated(null!, _root);
        Action emptySettings = () => factory.CreateIsolated(Context("command"), " ");

        nullContext.Should().Throw<ArgumentNullException>().WithParameterName("context");
        emptySettings.Should().Throw<ArgumentException>().WithParameterName("settingsLocation");
    }

    [Fact]
    public void Constructor_NullConfigurationPaths_Throws()
    {
        Action act = () => _ = new TemplaterFactory(null!, Components());

        act.Should().Throw<ArgumentNullException>().WithParameterName("configurationPaths");
    }

    [Fact]
    public void Constructor_NullComponentProvider_Throws()
    {
        Action act = () => _ = new TemplaterFactory(new CliConfigurationPathsOptions(Home), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("componentProvider");
    }

    [Fact]
    public void Create_RegistersComponentsFromInjectedProvider()
    {
        var component = Substitute.For<IGenerator>();
        component.Id.Returns(Guid.NewGuid());
        var provider = Substitute.For<IFuncTemplateComponentProvider>();
        ICoreTemplateComponent coreComponent = Substitute.For<ICoreTemplateComponent>();
        coreComponent.InterfaceType.Returns(typeof(IGenerator));
        coreComponent.Instance.Returns(component);
        provider.Get().Returns([coreComponent]);
        TemplaterFactory factory = new(new CliConfigurationPathsOptions(Home), provider);

        using Templater templater = factory.Create(Context("command"));

        templater.Settings.Components.OfType<IGenerator>().Should().ContainSingle().Which.Should().BeSameAs(component);
        provider.Received(1).Get();
    }

    private static FuncTemplateComponentsProvider Components() => new([], [], Substitute.For<IInteractionService>());

    private async Task InstallFixturesUnderHomeAsync()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, Path.Combine(Home, "templates"));
    }

    private TemplateEngineContext Context(string directory)
        => new(WorkingDirectory.FromExplicit(Path.Combine(_root, directory)));
}
