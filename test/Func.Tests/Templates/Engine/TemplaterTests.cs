// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using Xunit;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public class TemplaterTests : IDisposable
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

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Create_RegistersRunnableProjectsAndFuncConstraintComponents()
    {
        Templater templater = CreateTemplater();

        // RunnableProjects generator components are registered, so the host can
        // load and render standard template.json packages.
        Assert.NotEmpty(templater.Settings.Components.OfType<IGenerator>());

        // Every member of the func constraint factory component set is
        // registered on the host under the engine interface it is resolved by
        // (e.g. ITemplateConstraintFactory). The component manager caches by
        // that exact interface, so it must be probed with the registered type,
        // not the IIdentifiedComponent base (which is never a cache key).
        MethodInfo tryGetComponent = typeof(IComponentManager)
            .GetMethod(nameof(IComponentManager.TryGetComponent))!;

        foreach ((Type Type, IIdentifiedComponent Instance) in FuncTemplateComponents.AllComponents)
        {
            object?[] args = [Instance.Id, null];
            bool resolved = (bool)tryGetComponent
                .MakeGenericMethod(Type)
                .Invoke(templater.Settings.Components, args)!;

            Assert.True(resolved, $"{Instance.GetType().Name} was not resolvable as {Type.Name}.");
        }
    }

    [Fact]
    public async Task GetTemplatesAsync_ReturnsTemplatesInstalledInHive()
    {
        Templater templater = CreateTemplater();
        string packageDir = CreateSampleTemplatePackage();

        IManagedTemplatePackageProvider provider = templater.PackageManager
            .GetBuiltInManagedProvider(InstallationScope.Global);
        await provider.InstallAsync([new InstallRequest(packageDir)], CancellationToken.None);

        IReadOnlyList<ITemplateInfo> templates = await templater.GetTemplatesAsync();

        Assert.Contains(templates, t => t.Identity == "Func.Test.Sample");
    }

    private Templater CreateTemplater()
        => Templater.Create(
            new ProjectResolutionResult.NotResolved("test: no project resolved"),
            new ExtensionBundleResolution.NotResolved("test: no bundle installed"),
            settingsLocation: _hive);

    private string CreateSampleTemplatePackage()
    {
        string packageDir = Path.Combine(_root, "sample-package");
        string configDir = Path.Combine(packageDir, ".template.config");
        Directory.CreateDirectory(configDir);

        File.WriteAllText(Path.Combine(packageDir, "SampleFunction.txt"), "sample content");
        File.WriteAllText(
            Path.Combine(configDir, "template.json"),
            """
            {
              "$schema": "http://json.schemastore.org/template",
              "author": "func-tests",
              "classifications": [ "Test" ],
              "identity": "Func.Test.Sample",
              "name": "Func Test Sample",
              "shortName": "func-test-sample",
              "tags": { "type": "item" },
              "sourceName": "SampleFunction"
            }
            """);

        return packageDir;
    }
}
