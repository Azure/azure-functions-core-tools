// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Common;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public class OperationTemplatePackageTests
{
    private static string PackagePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "Templates", "Fixtures", "DotNet", $"{name}.nupkg");

    [Theory]
    [InlineData("project", "Microsoft.AzureFunctions.ProjectTemplate.CSharp.Isolated.3.x")]
    [InlineData("item", "Azure.Function.CSharp.Isolated.HttpTrigger.3.x")]
    public async Task BuiltInProvider_RegistersSealedRealArchive(string packageName, string identity)
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-operation-{Guid.NewGuid():N}");
        string source = Path.Combine(root, "source.nupkg");
        using PrivateTemplateFileSystem memory = new(root, Path.Combine(root, "output"));
        byte[] bytes = await File.ReadAllBytesAsync(PackagePath(packageName));
        memory.CreateDirectory(root);
        using (Stream output = memory.CreateFile(source))
        {
            await output.WriteAsync(bytes);
        }

        memory.SealSource(source);
        Assert.Throws<UnauthorizedAccessException>(() => memory.FileDelete(source));
        TemplateEngineContext context = new(WorkingDirectory.FromExplicit(root));
        using EngineEnvironmentSettings settings = new(new FuncTemplateEngineHost(context, memory), settingsLocation: Path.Combine(root, "settings"));
        using TemplatePackageManager manager = new(settings);
        IManagedTemplatePackageProvider provider = manager.GetBuiltInManagedProvider(InstallationScope.Global);
        IReadOnlyList<InstallResult> installed = await provider.InstallAsync([new InstallRequest(source)], CancellationToken.None);

        Assert.Single(installed);
        Assert.True(installed[0].Success, installed[0].ErrorMessage);
        foreach (ITemplatePackage package in await manager.GetTemplatePackagesAsync(false, CancellationToken.None))
        {
            memory.SealSource(package.MountPointUri);
        }

        IReadOnlyList<ITemplateInfo> templates = await manager.GetTemplatesAsync(CancellationToken.None);
        Assert.Contains(templates, template => template.Identity == identity);
        Assert.False(Directory.Exists(root));
    }

    [WindowsX64TemplateSourceTheory]
    [InlineData("net6.0", "1.3.3")]
    [InlineData("net10.0", "2.1.1")]
    public async Task CreateAsync_ItemTemplate_UsesOperationBindingForAuthoredCondition(string framework, string expectedVersion)
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-binding-test-{Guid.NewGuid():N}");
        try
        {
            string home = Path.Combine(root, "home");
            TemplateEngineContext context = new(
                WorkingDirectory.FromExplicit(root),
                bindings: new Dictionary<string, string> { ["msbuild:TargetFramework"] = framework });
            using (TemplateEngineSession registration = new(context, Path.Combine(home, "templates")))
            {
                var installed = await registration.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global)
                    .InstallAsync([new InstallRequest(PackagePath("item"))], CancellationToken.None);
                Assert.True(Assert.Single(installed).Success);
            }

            TemplaterFactory factory = new(new CliConfigurationPathsOptions(home), new InstalledProjectStackCatalog([]), new PhysicalFileSystem());
            using Templater operation = await factory.CreateAsync(context, CancellationToken.None);
            TemplateResolutionResult selection = await operation.ResolveAsync("Azure.Function.CSharp.Isolated.HttpTrigger.3.x", TemplateType.Item);
            Assert.Equal(TemplateResolutionStatus.Selected, selection.Status);
            TemplateRenderResult rendered = await operation.RenderAsync(
                selection.SelectedTemplate!, "OperationHttp", new Dictionary<string, string> { ["namespace"] = "OperationApp" }, CancellationToken.None);
            var reference = Assert.Single(rendered.PostActions.Where(action =>
                action.ActionId == Guid.Parse("B17581D1-C5C9-4489-8F0A-004BE667B814")));
            Assert.Equal(expectedVersion, reference.Arguments["version"]);
            var workload = Substitute.For<IProjectTemplateTasks>();
            workload.RequiresExecutionAsync(
                Arg.Any<WorkingDirectory>(), reference.ActionId, Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
                .Returns(true);
            var pending = await operation.PrepareTasksAsync(rendered, workload, CancellationToken.None);
            Assert.Equal(reference.ContinueOnError, Assert.Single(pending).ContinueOnError);
            await workload.Received(1).RequiresExecutionAsync(
                context.CommandDirectory, reference.ActionId, Arg.Any<IReadOnlyDictionary<string, string>>(), CancellationToken.None);
            await workload.DidNotReceive().ExecuteAsync(
                Arg.Any<WorkingDirectory>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteTasks_ContinuesOnlyWhenTemplateAllowsFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-task-order-{Guid.NewGuid():N}");
        TemplateEngineContext context = new(WorkingDirectory.FromExplicit(root));
        using Templater templater = new(
            new TemplateEngineSession(context, Path.Combine(root, "settings")),
            new InstalledProjectStackCatalog([]));
        IProjectTemplateTasks workload = Substitute.For<IProjectTemplateTasks>();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var tasks = new (Guid ActionId, IReadOnlyDictionary<string, string> Arguments, bool ContinueOnError)[]
        {
            (first, new Dictionary<string, string>(), true),
            (second, new Dictionary<string, string>(), false)
        };
        workload.ExecuteAsync(context.CommandDirectory, first, Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new GracefulException("optional task failed")));

        var failures = await templater.ExecuteTasksAsync(tasks, workload, CancellationToken.None);

        Assert.Equal(["optional task failed"], failures);
        await workload.Received(1).ExecuteAsync(context.CommandDirectory, first, Arg.Any<IReadOnlyDictionary<string, string>>(), CancellationToken.None);
        await workload.Received(1).ExecuteAsync(context.CommandDirectory, second, Arg.Any<IReadOnlyDictionary<string, string>>(), CancellationToken.None);

        workload.ClearReceivedCalls();
        workload.ExecuteAsync(context.CommandDirectory, first, Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new GracefulException("required task failed")));
        var requiredTasks = new (Guid ActionId, IReadOnlyDictionary<string, string> Arguments, bool ContinueOnError)[]
        {
            (first, new Dictionary<string, string>(), false),
            (second, new Dictionary<string, string>(), true)
        };

        GracefulException exception = await Assert.ThrowsAsync<GracefulException>(
            () => templater.ExecuteTasksAsync(requiredTasks, workload, CancellationToken.None));

        Assert.Equal("required task failed", exception.Message);
        await workload.DidNotReceive().ExecuteAsync(context.CommandDirectory, second, Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [WindowsX64TemplateSourceTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateAsync_UsesOnlyCapturedContentThroughGenerationAfterOriginalPackagesAreDeleted(bool projectTemplate)
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-operation-test-{Guid.NewGuid():N}");
        try
        {
            string home = Path.Combine(root, "home");
            string settingsLocation = Path.Combine(home, "templates");
            TemplateEngineContext context = new(
                WorkingDirectory.FromExplicit(root),
                bindings: new Dictionary<string, string> { ["msbuild:TargetFramework"] = "net10.0" });
            string[] installedPaths;
            using (TemplateEngineSession registration = new(context, settingsLocation))
            {
                IManagedTemplatePackageProvider provider = registration.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global);
                IReadOnlyList<InstallResult> results = await provider.InstallAsync(
                    [new InstallRequest(PackagePath("project")), new InstallRequest(PackagePath("item"))], CancellationToken.None);
                Assert.All(results, result => Assert.True(result.Success, result.ErrorMessage));
                installedPaths = [.. (await registration.PackageManager.GetTemplatePackagesAsync(false, CancellationToken.None))
                    .Select(package => package.MountPointUri)];
            }

            TemplaterFactory factory = new(new CliConfigurationPathsOptions(home), new InstalledProjectStackCatalog([]), new PhysicalFileSystem());
            using Templater operation = await factory.CreateAsync(context, CancellationToken.None);
            foreach (string path in installedPaths)
            {
                File.Delete(path);
            }

            TemplateResolutionResult project = await operation.ResolveAsync(
                "Microsoft.AzureFunctions.ProjectTemplate.CSharp.Isolated.3.x", TemplateType.Project);
            TemplateResolutionResult item = await operation.ResolveAsync("Azure.Function.CSharp.Isolated.HttpTrigger.3.x", TemplateType.Item);

            Assert.Equal(TemplateResolutionStatus.Selected, project.Status);
            Assert.Equal(TemplateResolutionStatus.Selected, item.Status);
            Assert.Equal("Microsoft.Azure.Functions.Worker.ProjectTemplates", project.SelectedTemplate!.Descriptor.Package!.Identifier, ignoreCase: true);
            Assert.Equal("4.0.5708", item.SelectedTemplate!.Descriptor.Package!.Version);
            Dictionary<string, string> parameters = projectTemplate
                ? new() { ["Framework"] = "net10.0" }
                : new() { ["namespace"] = "OperationApp", ["AccessRights"] = "Anonymous" };
            TemplateRenderResult rendered = await operation.RenderAsync(
                projectTemplate ? project.SelectedTemplate : item.SelectedTemplate,
                projectTemplate ? "OperationApp" : "OperationHttp", parameters, CancellationToken.None);
            string output = System.Text.Encoding.UTF8.GetString(
                rendered.Files[projectTemplate ? "OperationApp.csproj" : "OperationHttp.cs"].AsSpan());
            Assert.Contains(projectTemplate ? "<TargetFramework>net10.0</TargetFramework>" : "namespace OperationApp", output);
            Assert.Contains(projectTemplate ? "2.52.0" : "AuthorizationLevel.Anonymous", output);
            Assert.Contains(rendered.PostActions, action => action.ActionId == Guid.Parse(
                projectTemplate ? "210D431B-A78B-4D2F-B762-4ED3E3EA9025" : "B17581D1-C5C9-4489-8F0A-004BE667B814"));
            if (projectTemplate)
            {
                Assert.Contains("<PackageReference Include=\"Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore\" Version=\"2.1.1\" />", output);
                Assert.Contains(".gitignore", rendered.Files.Keys);
                Assert.Contains("OperationApp.csproj", rendered.Files.Keys);
            }
            else
            {
                var reference = Assert.Single(rendered.PostActions.Where(action =>
                    action.ActionId == Guid.Parse("B17581D1-C5C9-4489-8F0A-004BE667B814")));
                Assert.Equal("package", reference.Arguments["referenceType"]);
                Assert.Equal("Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore", reference.Arguments["reference"]);
                Assert.Equal("2.1.1", reference.Arguments["version"]);
                Assert.Equal(".csproj", reference.Arguments["projectFileExtensions"]);
            }
            Assert.False(Directory.Exists(Path.Combine(home, "template-operations")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("folder\\escape.txt")]
    [InlineData("content/ProjectTemplate-CSharp/.template.config/template.json")]
    [InlineData("CONTENT/ProjectTemplate-CSharp/new.txt")]
    public async Task CreateAsync_RejectsUnsafeOrAliasedArchiveEntriesBeforeDiscovery(string entryName)
    {
        await AssertRejectedCaptureAsync(bytes =>
        {
            using MemoryStream content = new();
            content.Write(bytes);
            using (ZipArchive archive = new(content, ZipArchiveMode.Update, leaveOpen: true))
            {
                using Stream output = archive.CreateEntry(entryName).Open();
                output.WriteByte(1);
            }

            return content.ToArray();
        });
    }

    [Theory]
    [InlineData("compressed")]
    [InlineData("expanded")]
    [InlineData("configuration")]
    [InlineData("entries")]
    [InlineData("link")]
    public async Task CreateAsync_RejectsResourceOverflowAndLinksBeforeDiscovery(string violation)
    {
        await AssertRejectedCaptureAsync(bytes =>
        {
            if (violation == "compressed")
            {
                return new byte[TemplaterFactory.MaximumArchiveBytes + 1];
            }

            using MemoryStream content = new();
            content.Write(bytes);
            using (ZipArchive archive = new(content, ZipArchiveMode.Update, leaveOpen: true))
            {
                if (violation == "entries")
                {
                    for (int index = 0; index <= TemplaterFactory.MaximumEntryCount; index++)
                    {
                        archive.CreateEntry($"extra/{index}.txt");
                    }
                }
                else
                {
                    ZipArchiveEntry entry = archive.CreateEntry(violation == "configuration"
                        ? "extra/.template.config/large.json" : "extra/file.txt");
                    if (violation == "link")
                    {
                        entry.ExternalAttributes = 0xa000 << 16;
                    }
                    else
                    {
                        using Stream output = entry.Open();
                        output.Write(new byte[(violation == "expanded"
                            ? TemplaterFactory.MaximumEntryBytes : TemplaterFactory.MaximumConfigurationBytes) + 1]);
                    }
                }
            }

            return content.ToArray();
        });
    }

    private async Task AssertRejectedCaptureAsync(Func<byte[], byte[]> change)
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-rejected-capture-{Guid.NewGuid():N}");
        try
        {
            string home = Path.Combine(root, "home");
            TemplateEngineContext context = new(WorkingDirectory.FromExplicit(root));
            using (TemplateEngineSession registration = new(context, Path.Combine(home, "templates")))
            {
                IReadOnlyList<InstallResult> installed = await registration.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global)
                    .InstallAsync([new InstallRequest(PackagePath("project"))], CancellationToken.None);
                Assert.True(Assert.Single(installed).Success);
            }

            byte[] invalid = change(await File.ReadAllBytesAsync(PackagePath("project")));
            IFileSystem fileSystem = Substitute.For<IFileSystem>();
            fileSystem.OpenReadNoFollow(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => new MemoryStream(invalid, writable: false));
            TemplaterFactory factory = new(new CliConfigurationPathsOptions(home), new InstalledProjectStackCatalog([]), fileSystem);

            await Assert.ThrowsAsync<InvalidDataException>(() => factory.CreateAsync(context, CancellationToken.None));
            Assert.False(Directory.Exists(Path.Combine(home, "template-operations")));
            fileSystem.Received(1).OpenReadNoFollow(Arg.Any<string>(), CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(false, "duplicate-identity")]
    [InlineData(true, "invalid-template-configuration")]
    public async Task CapturedCatalog_RejectsDuplicatesAndInvalidTemplates(bool invalidConfiguration, string diagnosticCode)
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-catalog-integrity-{Guid.NewGuid():N}");
        try
        {
            string home = Path.Combine(root, "home");
            TemplateEngineContext context = new(WorkingDirectory.FromExplicit(root));
            Dictionary<string, byte[]> packages = new(StringComparer.Ordinal);
            byte[] projectBytes = await File.ReadAllBytesAsync(PackagePath("project"));
            using MemoryStream changedItem = new();
            changedItem.Write(await File.ReadAllBytesAsync(PackagePath("item")));
            using (ZipArchive archive = new(changedItem, ZipArchiveMode.Update, leaveOpen: true))
            {
                const string configurationPath = "content/HttpTrigger-CSharp/.template.config/template.json";
                ZipArchiveEntry configuration = archive.GetEntry(configurationPath)!;
                string json;
                using (StreamReader reader = new(configuration.Open()))
                {
                    json = await reader.ReadToEndAsync();
                }

                configuration.Delete();
                using StreamWriter output = new(archive.CreateEntry(configurationPath).Open());
                await output.WriteAsync(invalidConfiguration ? "{ malformed }" : json.Replace(
                    "Azure.Function.CSharp.Isolated.HttpTrigger.3.x",
                    "Microsoft.AzureFunctions.ProjectTemplate.CSharp.Isolated.3.x", StringComparison.Ordinal));
            }

            byte[] itemBytes = changedItem.ToArray();
            using (TemplateEngineSession registration = new(context, Path.Combine(home, "templates")))
            {
                IReadOnlyList<InstallResult> installed = await registration.PackageManager.GetBuiltInManagedProvider(InstallationScope.Global)
                    .InstallAsync([new InstallRequest(PackagePath("project")), new InstallRequest(PackagePath("item"))], CancellationToken.None);
                Assert.All(installed, result => Assert.True(result.Success, result.ErrorMessage));
                foreach (ITemplatePackage package in await registration.PackageManager.GetTemplatePackagesAsync(false, CancellationToken.None))
                {
                    packages.Add(package.MountPointUri, ((IManagedTemplatePackage)package).Identifier.EndsWith("ProjectTemplates", StringComparison.OrdinalIgnoreCase)
                        ? projectBytes : itemBytes);
                }
            }

            IFileSystem fileSystem = Substitute.For<IFileSystem>();
            fileSystem.OpenReadNoFollow(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => new MemoryStream(packages[call.ArgAt<string>(0)], writable: false));
            TemplaterFactory factory = new(new CliConfigurationPathsOptions(home), new InstalledProjectStackCatalog([]), fileSystem);
            using Templater operation = await factory.CreateAsync(context, CancellationToken.None);

            TemplateResolutionResult result = await operation.ResolveAsync(
                "Microsoft.AzureFunctions.ProjectTemplate.CSharp.Isolated.3.x", TemplateType.Project);

            Assert.Equal(TemplateResolutionStatus.Failed, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == diagnosticCode);
            Assert.False(Directory.Exists(Path.Combine(home, "template-operations")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

}
