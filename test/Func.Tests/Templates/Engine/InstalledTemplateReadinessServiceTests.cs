// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using NSubstitute;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class InstalledTemplateReadinessServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("template-readiness-").FullName;
    private readonly string _contentRoot;
    private readonly string _home;

    public InstalledTemplateReadinessServiceTests()
    {
        _contentRoot = Path.Combine(_root, "content");
        _home = Path.Combine(_root, "home");
        Directory.CreateDirectory(Path.Combine(_contentRoot, "templates"));
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureReadyAsync_RegistersAndVerifiesCompleteRequiredPackageSet()
    {
        IReadOnlyList<WorkloadTemplatePackage> required = RequiredPackages();
        CreatePackage(required[0].PackageId, required[0].Version, required[0].RelativePath);
        CreatePackage(required[1].PackageId, required[1].Version, required[1].RelativePath);
        InstalledTemplateReadinessService service = CreateService(required, out Workload workload);

        await service.EnsureReadyAsync(Entry(), CancellationToken.None);

        IManagedTemplatePackage[] packages = await GetRegisteredPackagesAsync();
        packages.Should().ContainSingle(package =>
            package.Identifier == required[0].PackageId && package.Version == required[0].Version);
        packages.Should().ContainSingle(package =>
            package.Identifier == required[1].PackageId && package.Version == required[1].Version);
        workload.Received(1).Configure(Arg.Any<FunctionsCliBuilder>());
    }

    [Theory]
    [InlineData("project.nupkg", "Microsoft.Azure.Functions.Worker.ItemTemplates")]
    [InlineData("item.nupkg", "Microsoft.Azure.Functions.Worker.ProjectTemplates")]
    public async Task EnsureReadyAsync_MissingOneRequiredPackage_FailsClosed(string presentFile, string missingPackageId)
    {
        IReadOnlyList<WorkloadTemplatePackage> required = RequiredPackages();
        WorkloadTemplatePackage present = required.Single(package =>
            Path.GetFileName(package.RelativePath) == presentFile);
        CreatePackage(present.PackageId, present.Version, present.RelativePath);
        InstalledTemplateReadinessService service = CreateService(required, out _);

        Func<Task> act = () => service.EnsureReadyAsync(Entry(), CancellationToken.None);

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage($"*{missingPackageId}*");
        (await GetRegisteredPackagesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureReadyAsync_WrongSourcePackageIdentityOrVersion_FailsClosed()
    {
        IReadOnlyList<WorkloadTemplatePackage> required = RequiredPackages();
        CreatePackage("Different.Package", required[0].Version, required[0].RelativePath);
        CreatePackage(required[1].PackageId, required[1].Version, required[1].RelativePath);
        InstalledTemplateReadinessService service = CreateService(required, out _);

        Func<Task> act = () => service.EnsureReadyAsync(Entry(), CancellationToken.None);

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>()
            .WithMessage("*does not match its declared package identity and version*");
        (await GetRegisteredPackagesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureReadyAsync_WrongSourcePackageVersion_FailsClosed()
    {
        IReadOnlyList<WorkloadTemplatePackage> required = RequiredPackages();
        CreatePackage(required[0].PackageId, "4.0.5707", required[0].RelativePath);
        CreatePackage(required[1].PackageId, required[1].Version, required[1].RelativePath);
        InstalledTemplateReadinessService service = CreateService(required, out _);

        Func<Task> act = () => service.EnsureReadyAsync(Entry(), CancellationToken.None);

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>()
            .WithMessage("*does not match its declared package identity and version*");
        (await GetRegisteredPackagesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureReadyAsync_CorruptPackage_FailsClosed()
    {
        IReadOnlyList<WorkloadTemplatePackage> required = RequiredPackages();
        File.WriteAllText(Path.Combine(_contentRoot, required[0].RelativePath), "not a package");
        CreatePackage(required[1].PackageId, required[1].Version, required[1].RelativePath);
        InstalledTemplateReadinessService service = CreateService(required, out _);

        Func<Task> act = () => service.EnsureReadyAsync(Entry(), CancellationToken.None);

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>();
        (await GetRegisteredPackagesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureReadyAsync_WorkloadConfigurationFailure_PreventsRegistration()
    {
        IReadOnlyList<WorkloadTemplatePackage> required = RequiredPackages();
        CreatePackage(required[0].PackageId, required[0].Version, required[0].RelativePath);
        CreatePackage(required[1].PackageId, required[1].Version, required[1].RelativePath);
        InstalledTemplateReadinessService service = CreateService(required, out Workload workload);
        workload.When(instance => instance.Configure(Arg.Any<FunctionsCliBuilder>()))
            .Do(_ => throw new InvalidOperationException("configuration failed"));

        Func<Task> act = () => service.EnsureReadyAsync(Entry(), CancellationToken.None);

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage("*configuration failed*");
        (await GetRegisteredPackagesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureReadyAsync_TemplateEngineRegistrationFailure_FailsClosed()
    {
        IReadOnlyList<WorkloadTemplatePackage> required = RequiredPackages();
        CreatePackage(required[0].PackageId, required[0].Version, required[0].RelativePath);
        CreatePackage(required[1].PackageId, required[1].Version, required[1].RelativePath);
        File.Delete(Path.Combine(_home, "templates"));
        File.WriteAllText(Path.Combine(_home, "templates"), "blocks the settings directory");
        InstalledTemplateReadinessService service = CreateService(required, out _);

        Func<Task> act = () => service.EnsureReadyAsync(Entry(), CancellationToken.None);

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>()
            .WithMessage("*template readiness failed*");
    }

    [Fact]
    public async Task EnsureReadyAsync_DuplicateRequiredIdentity_FailsClosed()
    {
        IReadOnlyList<WorkloadTemplatePackage> required =
        [
            new("Microsoft.Azure.Functions.Worker.ProjectTemplates", "4.0.5708", Path.Combine("templates", "project.nupkg")),
            new("microsoft.azure.functions.worker.projecttemplates", "4.0.5708", Path.Combine("templates", "duplicate.nupkg")),
        ];
        CreatePackage(required[0].PackageId, required[0].Version, required[0].RelativePath);
        CreatePackage(required[1].PackageId, required[1].Version, required[1].RelativePath);
        InstalledTemplateReadinessService service = CreateService(required, out _);

        Func<Task> act = () => service.EnsureReadyAsync(Entry(), CancellationToken.None);

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>()
            .WithMessage("*duplicate required template package identities*");
        (await GetRegisteredPackagesAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("project.nupkg")]
    [InlineData("item.nupkg")]
    public async Task EnsureReadyAsync_PartialRegisteredSet_RemainsNotReady(string presentFile)
    {
        IReadOnlyList<WorkloadTemplatePackage> required = RequiredPackages();
        WorkloadTemplatePackage present = required.Single(package =>
            Path.GetFileName(package.RelativePath) == presentFile);
        string packagePath = CreatePackage(present.PackageId, present.Version, present.RelativePath);
        await InstallPackageAsync(packagePath);
        InstalledTemplateReadinessService service = CreateService(required, out _);

        Func<Task> act = () => service.EnsureReadyAsync(Entry(), CancellationToken.None);

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>();
        IManagedTemplatePackage[] packages = await GetRegisteredPackagesAsync();
        packages.Should().ContainSingle(package =>
            package.Identifier == present.PackageId && package.Version == present.Version);
    }

    [Fact]
    public async Task EnsureReadyAsync_WrongRegisteredVersion_FailsClosed()
    {
        IReadOnlyList<WorkloadTemplatePackage> required = RequiredPackages();
        string wrongVersionPath = CreatePackage(
            required[0].PackageId,
            "4.0.5707",
            Path.Combine("templates", "wrong-version.nupkg"));
        CreatePackage(required[0].PackageId, required[0].Version, required[0].RelativePath);
        CreatePackage(required[1].PackageId, required[1].Version, required[1].RelativePath);
        await InstallPackageAsync(wrongVersionPath);
        InstalledTemplateReadinessService service = CreateService(required, out _);

        Func<Task> act = () => service.EnsureReadyAsync(Entry(), CancellationToken.None);

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>()
            .WithMessage("*different version*");
        IManagedTemplatePackage[] packages = await GetRegisteredPackagesAsync();
        packages.Should().ContainSingle(package =>
            package.Identifier == required[0].PackageId && package.Version == "4.0.5707");
    }

    private InstalledTemplateReadinessService CreateService(
        IReadOnlyList<WorkloadTemplatePackage> required,
        out Workload workload)
    {
        workload = Substitute.For<Workload>();
        workload.RequiredTemplatePackages.Returns(required);
        workload.DisplayName.Returns("Test workload");
        workload.Description.Returns("Test workload.");

        WorkloadLoadContext loadContext = new("test-workload", typeof(InstalledTemplateReadinessServiceTests).Assembly.Location);
        RuntimeWorkloadInfo runtime = new(
            workload,
            "test.workload",
            "1.0.0",
            [],
            _root,
            _contentRoot,
            workload.DisplayName,
            workload.Description,
            loadContext);
        IWorkloadLoader loader = Substitute.For<IWorkloadLoader>();
        loader.Load(Arg.Any<IReadOnlyList<WorkloadEntry>>()).Returns([runtime]);
        return new InstalledTemplateReadinessService(loader, new CliConfigurationPathsOptions(_home));
    }

    private WorkloadEntry Entry() => new()
    {
        PackageId = "test.workload",
        PackageVersion = "1.0.0",
        Kind = WorkloadKind.Workload,
        Aliases = [],
        EntryPoint = new EntryPointSpec { AssemblyPath = "Test.dll", Type = "Test.Workload" },
        InstallRefCount = 1,
    };

    private IReadOnlyList<WorkloadTemplatePackage> RequiredPackages() =>
    [
        new("Microsoft.Azure.Functions.Worker.ProjectTemplates", "4.0.5708", Path.Combine("templates", "project.nupkg")),
        new("Microsoft.Azure.Functions.Worker.ItemTemplates", "4.0.5708", Path.Combine("templates", "item.nupkg")),
    ];

    private string CreatePackage(string packageId, string version, string relativePath)
    {
        string payload = Path.Combine(_root, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(payload, """{"$schema":"http://json.schemastore.org/template","identity":"test.template"}""");
        var builder = new PackageBuilder
        {
            Id = packageId,
            Version = NuGetVersion.Parse(version),
            Description = "Template package test fixture.",
        };
        builder.Authors.Add("test");
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = payload,
            TargetPath = ".template.config/template.json",
        });

        string path = Path.Combine(_contentRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream stream = File.Create(path);
        builder.Save(stream);
        return path;
    }

    private async Task InstallPackageAsync(string packagePath)
    {
        TemplateEngineContext context = new(WorkingDirectory.FromExplicit(_contentRoot));
        using TemplateEngineSession session = new(context, Path.Combine(_home, "templates"));
        IReadOnlyList<InstallResult> result = await session.PackageManager
            .GetBuiltInManagedProvider(InstallationScope.Global)
            .InstallAsync([new InstallRequest(packagePath)], CancellationToken.None);
        Assert.True(Assert.Single(result).Success, result[0].ErrorMessage);
    }

    private async Task<IManagedTemplatePackage[]> GetRegisteredPackagesAsync()
    {
        if (!Directory.Exists(Path.Combine(_home, "templates")))
        {
            return [];
        }

        TemplateEngineContext context = new(WorkingDirectory.FromExplicit(_contentRoot));
        using TemplateEngineSession session = new(context, Path.Combine(_home, "templates"));
        IReadOnlyList<ITemplatePackage> packages = await session.PackageManager.GetTemplatePackagesAsync(
            force: false,
            CancellationToken.None);
        return [.. packages.OfType<IManagedTemplatePackage>()];
    }
}
