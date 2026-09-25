// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;

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
        TemplaterFactory factory = new(new CliConfigurationPathsOptions(Home));
        TemplateEngineContext context = Context("command");

        using Templater templater = factory.Create(context);

        templater.Context.Should().BeSameAs(context);
    }

    [Fact]
    public async Task Create_ReadsTemplatesFromTemplatesDirectoryUnderFuncHome()
    {
        await InstallFixturesUnderHomeAsync();
        TemplaterFactory factory = new(new CliConfigurationPathsOptions(Home));

        using Templater templater = factory.Create(Context("command"));
        IReadOnlyList<ITemplateInfo> templates = await templater.GetTemplatesAsync(CancellationToken.None);

        templates.Should().Contain(template => template.Identity == TemplateFixtures.ItemTemplateIdentity);
    }

    [Fact]
    public async Task Create_EachCallReturnsAnIndependentTemplater()
    {
        await InstallFixturesUnderHomeAsync();
        TemplaterFactory factory = new(new CliConfigurationPathsOptions(Home));
        Templater first = factory.Create(Context("first"));
        using Templater second = factory.Create(Context("second"));

        first.Dispose();
        IReadOnlyList<ITemplateInfo> templates = await second.GetTemplatesAsync(CancellationToken.None);

        templates.Should().Contain(template => template.Identity == TemplateFixtures.ItemTemplateIdentity);
    }

    [Fact]
    public void Create_NullContext_Throws()
    {
        TemplaterFactory factory = new(new CliConfigurationPathsOptions(Home));

        Action act = () => factory.Create(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public void Constructor_NullConfigurationPaths_Throws()
    {
        Action act = () => _ = new TemplaterFactory(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configurationPaths");
    }

    private async Task InstallFixturesUnderHomeAsync()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, Path.Combine(Home, "templates"));
    }

    private TemplateEngineContext Context(string directory)
        => new(WorkingDirectory.FromExplicit(Path.Combine(_root, directory)));
}
