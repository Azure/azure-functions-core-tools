// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.TemplateEngine.Abstractions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

/// <summary>
/// Exercises the engine-managed package service against the real templating
/// engine and the real local template feed. Every test redirects the func home
/// and user profile to a throwaway directory so it never touches the developer's
/// <c>~/.azure-functions</c> or <c>~/.templateengine</c>.
/// </summary>
public class FuncTemplatePackageServiceTests : IDisposable
{
    private const string PythonPackageId = "Microsoft.Azure.Functions.Templates.Python";
    private const string NodePackageId = "Microsoft.Azure.Functions.Templates.Node";
    private const string FixtureVersion = "1.0.0";

    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables)
        {
            disposable.Dispose();
        }

        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup: a hive file may still be held by the OS after disposal.
            }
        }
    }

    [Fact]
    public async Task InstallAsync_NullRequest_Throws()
    {
        FuncTemplatePackageService service = CreateServiceWithSubstitutes();

        await FluentActions.Awaiting(() => service.InstallAsync(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InstallAsync_BlankPackageIdentifier_Throws(string identifier)
    {
        FuncTemplatePackageService service = CreateServiceWithSubstitutes();

        await FluentActions.Awaiting(() => service.InstallAsync(new TemplatePackageInstallRequest(identifier), CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task UninstallAsync_BlankPackageId_Throws(string? packageId)
    {
        FuncTemplatePackageService service = CreateServiceWithSubstitutes();

        await FluentActions.Awaiting(() => service.UninstallAsync(packageId!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpdateAsync_NullRequest_Throws()
    {
        FuncTemplatePackageService service = CreateServiceWithSubstitutes();

        await FluentActions.Awaiting(() => service.UpdateAsync(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateAsync_SinglePackageWithoutId_Throws()
    {
        FuncTemplatePackageService service = CreateServiceWithSubstitutes();

        await FluentActions.Awaiting(() => service.UpdateAsync(new TemplatePackageUpdateRequest(), CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task InstallAsync_FromLocalFeed_InstallsPackageAndListsIt()
    {
        (FuncTemplatePackageService service, _, string userProfile) = CreateService();
        string feed = LocateLocalFeed();

        TemplatePackageInstallResult result = await service.InstallAsync(
            new TemplatePackageInstallRequest(PythonPackageId, FixtureVersion, feed), CancellationToken.None);

        TemplatePackageInstallResult.Installed installed = result.Should().BeOfType<TemplatePackageInstallResult.Installed>().Subject;
        installed.Package.Identifier.Should().Be(PythonPackageId);
        installed.Package.Version.Should().Be(FixtureVersion);

        IReadOnlyList<InstalledTemplatePackage> list = await service.ListInstalledAsync(CancellationToken.None);
        list.Should().ContainSingle(p => p.Identifier == PythonPackageId);

        AssertDotnetNewStateUntouched(userProfile);
    }

    [Fact]
    public async Task InstallAsync_UnknownPackage_ReturnsNotFound()
    {
        (FuncTemplatePackageService service, _, _) = CreateService();
        string feed = LocateLocalFeed();

        TemplatePackageInstallResult result = await service.InstallAsync(
            new TemplatePackageInstallRequest("Contoso.Nonexistent.Templates", "1.0.0", feed), CancellationToken.None);

        result.Should().BeOfType<TemplatePackageInstallResult.NotFound>();
    }

    [Fact]
    public async Task InstallAsync_SameVersionTwice_ReturnsAlreadyInstalled()
    {
        (FuncTemplatePackageService service, _, _) = CreateService();
        string feed = LocateLocalFeed();
        var request = new TemplatePackageInstallRequest(PythonPackageId, FixtureVersion, feed);

        (await service.InstallAsync(request, CancellationToken.None))
            .Should().BeOfType<TemplatePackageInstallResult.Installed>();

        TemplatePackageInstallResult second = await service.InstallAsync(request, CancellationToken.None);

        TemplatePackageInstallResult.AlreadyInstalled already =
            second.Should().BeOfType<TemplatePackageInstallResult.AlreadyInstalled>().Subject;
        already.Package.Identifier.Should().Be(PythonPackageId);
    }

    [Fact]
    public async Task UninstallAsync_InstalledPackage_RemovesIt()
    {
        (FuncTemplatePackageService service, _, _) = CreateService();
        string feed = LocateLocalFeed();
        await service.InstallAsync(new TemplatePackageInstallRequest(PythonPackageId, FixtureVersion, feed), CancellationToken.None);

        TemplatePackageUninstallResult result = await service.UninstallAsync(PythonPackageId, CancellationToken.None);

        result.Should().BeOfType<TemplatePackageUninstallResult.Uninstalled>();
        (await service.ListInstalledAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task UninstallAsync_NotInstalled_ReturnsNotInstalled()
    {
        (FuncTemplatePackageService service, _, _) = CreateService();

        TemplatePackageUninstallResult result = await service.UninstallAsync("Contoso.Not.Installed", CancellationToken.None);

        result.Should().BeOfType<TemplatePackageUninstallResult.NotInstalled>();
    }

    [Fact]
    public async Task ListInstalledAsync_ReflectsInstallThenUninstall()
    {
        (FuncTemplatePackageService service, _, _) = CreateService();
        string feed = LocateLocalFeed();

        (await service.ListInstalledAsync(CancellationToken.None)).Should().BeEmpty();

        await service.InstallAsync(new TemplatePackageInstallRequest(PythonPackageId, FixtureVersion, feed), CancellationToken.None);
        (await service.ListInstalledAsync(CancellationToken.None)).Should().ContainSingle(p => p.Identifier == PythonPackageId);

        await service.UninstallAsync(PythonPackageId, CancellationToken.None);
        (await service.ListInstalledAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_WhenNewerVersionAvailable_UpdatesToLatest()
    {
        (FuncTemplatePackageService service, _, _) = CreateService();
        string feed = CreateFeedWithVersions(PythonPackageId, "1.0.0", "2.0.0");

        (await service.InstallAsync(new TemplatePackageInstallRequest(PythonPackageId, "1.0.0", feed), CancellationToken.None))
            .Should().BeOfType<TemplatePackageInstallResult.Installed>();

        TemplatePackageUpdateResult result = await service.UpdateAsync(
            new TemplatePackageUpdateRequest(PackageId: PythonPackageId), CancellationToken.None);

        TemplatePackageUpdateResult.Updated updated = result.Should().BeOfType<TemplatePackageUpdateResult.Updated>().Subject;
        updated.Packages.Should().ContainSingle();
        updated.Packages[0].PreviousVersion.Should().Be("1.0.0");
        updated.Packages[0].NewVersion.Should().Be("2.0.0");

        (await service.ListInstalledAsync(CancellationToken.None))
            .Should().ContainSingle(p => p.Identifier == PythonPackageId && p.Version == "2.0.0");
    }

    [Fact]
    public async Task UpdateAsync_WhenAlreadyLatest_ReturnsNoUpdatesAvailable()
    {
        (FuncTemplatePackageService service, _, _) = CreateService();
        string feed = CreateFeedWithVersions(PythonPackageId, "1.0.0");
        await service.InstallAsync(new TemplatePackageInstallRequest(PythonPackageId, "1.0.0", feed), CancellationToken.None);

        TemplatePackageUpdateResult result = await service.UpdateAsync(new TemplatePackageUpdateRequest(All: true), CancellationToken.None);

        result.Should().BeOfType<TemplatePackageUpdateResult.NoUpdatesAvailable>();
    }

    [Fact]
    public async Task UpdateAsync_UnknownPackage_ReturnsNotInstalled()
    {
        (FuncTemplatePackageService service, _, _) = CreateService();

        TemplatePackageUpdateResult result = await service.UpdateAsync(
            new TemplatePackageUpdateRequest(PackageId: "Contoso.Not.Installed"), CancellationToken.None);

        result.Should().BeOfType<TemplatePackageUpdateResult.NotInstalled>();
    }

    [Fact]
    public async Task InstallAsync_ConcurrentInstalls_BothLandCoherently()
    {
        (FuncTemplatePackageService service, _, _) = CreateService();
        string feed = LocateLocalFeed();

        // Warm the managed provider so provider resolution isn't itself a race; the
        // hive lock is what must keep the two concurrent installs coherent.
        await service.ListInstalledAsync(CancellationToken.None);

        Task<TemplatePackageInstallResult> python = service.InstallAsync(
            new TemplatePackageInstallRequest(PythonPackageId, FixtureVersion, feed), CancellationToken.None);
        Task<TemplatePackageInstallResult> node = service.InstallAsync(
            new TemplatePackageInstallRequest(NodePackageId, FixtureVersion, feed), CancellationToken.None);

        TemplatePackageInstallResult[] results = await Task.WhenAll(python, node);

        results[0].Should().BeOfType<TemplatePackageInstallResult.Installed>();
        results[1].Should().BeOfType<TemplatePackageInstallResult.Installed>();

        IReadOnlyList<InstalledTemplatePackage> installed = await service.ListInstalledAsync(CancellationToken.None);
        installed.Select(p => p.Identifier).Should().BeEquivalentTo([PythonPackageId, NodePackageId]);
    }

    [Fact]
    public async Task InstallFromLocalFeed_SurfacesTemplates_ThenUninstall()
    {
        (FuncTemplatePackageService service, FuncTemplateEngineSession session, string userProfile) = CreateService();
        string feed = LocateLocalFeed();

        await service.InstallAsync(new TemplatePackageInstallRequest(PythonPackageId, FixtureVersion, feed), CancellationToken.None);

        await session.PackageManager.RebuildTemplateCacheAsync(CancellationToken.None);
        IReadOnlyList<ITemplateInfo> afterInstall = await session.PackageManager.GetTemplatesAsync(CancellationToken.None);
        afterInstall.Should().NotBeEmpty("installing the Python package must surface its templates through the engine cache");

        TemplatePackageUninstallResult uninstall = await service.UninstallAsync(PythonPackageId, CancellationToken.None);
        uninstall.Should().BeOfType<TemplatePackageUninstallResult.Uninstalled>();

        await session.PackageManager.RebuildTemplateCacheAsync(CancellationToken.None);
        IReadOnlyList<ITemplateInfo> afterUninstall = await session.PackageManager.GetTemplatesAsync(CancellationToken.None);
        afterUninstall.Should().BeEmpty("uninstalling the package removes its templates from the cache");

        AssertDotnetNewStateUntouched(userProfile);
    }

    private static FuncTemplatePackageService CreateServiceWithSubstitutes()
    {
        var session = Substitute.For<IFuncTemplateEngineSession>();
        var hiveLock = Substitute.For<IFuncTemplateHiveLock>();
        return new FuncTemplatePackageService(session, hiveLock, NullLogger<FuncTemplatePackageService>.Instance);
    }

    private static void AssertDotnetNewStateUntouched(string userProfile)
    {
        Directory.Exists(Path.Combine(userProfile, ".templateengine")).Should()
            .BeFalse("the engine must never touch the user's dotnet new hive");
    }

    private static string LocateLocalFeed()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "artifacts", "local-template-feed");
            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*.nupkg").Any())
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Local template feed not found. Run 'pwsh eng/scripts/build-local-template-feed.ps1' to build it.");
    }

    private static void CloneNupkgWithVersion(string sourceNupkg, string version, string destinationNupkg)
    {
        using FileStream sourceStream = File.OpenRead(sourceNupkg);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read);
        using FileStream destinationStream = File.Create(destinationNupkg);
        using var destination = new ZipArchive(destinationStream, ZipArchiveMode.Create);

        foreach (ZipArchiveEntry entry in source.Entries)
        {
            ZipArchiveEntry copy = destination.CreateEntry(entry.FullName, CompressionLevel.Optimal);
            using Stream input = entry.Open();
            using Stream output = copy.Open();
            if (entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(input);
                string rewritten = Regex.Replace(
                    reader.ReadToEnd(),
                    "<version>.*?</version>",
                    $"<version>{version}</version>",
                    RegexOptions.Singleline);
                byte[] bytes = Encoding.UTF8.GetBytes(rewritten);
                output.Write(bytes, 0, bytes.Length);
            }
            else
            {
                input.CopyTo(output);
            }
        }
    }

    private string CreateFeedWithVersions(string packageId, params string[] versions)
    {
        string source = Path.Combine(LocateLocalFeed(), $"{packageId}.{FixtureVersion}.nupkg");
        string feedDirectory = Path.Combine(_root, Guid.NewGuid().ToString("N"), "feed");
        Directory.CreateDirectory(feedDirectory);

        foreach (string version in versions)
        {
            CloneNupkgWithVersion(source, version, Path.Combine(feedDirectory, $"{packageId}.{version}.nupkg"));
        }

        return feedDirectory;
    }

    private (FuncTemplatePackageService Service, FuncTemplateEngineSession Session, string UserProfile) CreateService()
    {
        string id = Guid.NewGuid().ToString("N");
        string funcHome = Path.Combine(_root, id, "func-home");
        string userProfile = Path.Combine(_root, id, "user-profile");
        Directory.CreateDirectory(userProfile);

        var paths = new FuncTemplateEnginePaths(funcHome, userProfile, "1.0.0-test");
        var host = new FuncTemplateEngineHost(NullLoggerFactory.Instance, Version(), []);
        var session = new FuncTemplateEngineSession(host, paths);
        var hiveLock = new FuncTemplateHiveLock(paths, NullLogger<FuncTemplateHiveLock>.Instance);
        var service = new FuncTemplatePackageService(session, hiveLock, NullLogger<FuncTemplatePackageService>.Instance);

        _disposables.Add(session);
        _disposables.Add(host);
        return (service, session, userProfile);
    }

    private static IFuncTemplateEngineVersion Version()
    {
        var version = Substitute.For<IFuncTemplateEngineVersion>();
        version.Version.Returns("1.0.0-test");
        return version;
    }
}
