// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Components;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

/// <summary>
/// Builds a real templating-engine session over the real local template feed,
/// wired with the func engine components (extension-bundle constraint and
/// msbuild bind source). Every instance redirects func home and the user
/// profile to a throwaway directory so tests never touch the developer's
/// <c>~/.azure-functions</c> or <c>~/.templateengine</c>.
/// </summary>
internal sealed class EngineIntegrationHarness : IDisposable
{
    public const string NodePackageId = "Microsoft.Azure.Functions.Templates.Node";
    public const string PythonPackageId = "Microsoft.Azure.Functions.Templates.Python";
    public const string ExtensionBundleId = "Microsoft.Azure.Functions.ExtensionBundle";

    private const string FixtureVersion = "1.0.0";

    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _disposables = [];
    private readonly FuncTemplatePackageService _packageService;

    public EngineIntegrationHarness()
    {
        string id = Guid.NewGuid().ToString("N");
        string funcHome = Path.Combine(_root, id, "func-home");
        UserProfile = Path.Combine(_root, id, "user-profile");
        Directory.CreateDirectory(UserProfile);

        var fileSystem = new PhysicalFuncTemplateFileSystem();
        List<FuncEngineComponent> components =
        [
            new FuncEngineComponent(typeof(ITemplateConstraintFactory), new ExtensionBundleConstraintFactory(BundleAccessor)),
            new FuncEngineComponent(
                typeof(IBindSymbolSource),
                new MsBuildBindSymbolSource(ProjectDirectoryAccessor, new MsBuildProjectFilePropertyReader(fileSystem))),
        ];

        var paths = new FuncTemplateEnginePaths(funcHome, UserProfile, "1.0.0-test");
        var version = Substitute.For<IFuncTemplateEngineVersion>();
        version.Version.Returns("1.0.0-test");
        var host = new FuncTemplateEngineHost(NullLoggerFactory.Instance, version, components);
        Session = new FuncTemplateEngineSession(host, paths);
        var hiveLock = new FuncTemplateHiveLock(paths, NullLogger<FuncTemplateHiveLock>.Instance);
        _packageService = new FuncTemplatePackageService(Session, hiveLock, NullLogger<FuncTemplatePackageService>.Instance);

        _disposables.Add(Session);
        _disposables.Add(host);
    }

    public FuncTemplateEngineSession Session { get; }

    public FuncExtensionBundleContextAccessor BundleAccessor { get; } = new();

    public FuncProjectDirectoryAccessor ProjectDirectoryAccessor { get; } = new();

    public string UserProfile { get; }

    /// <summary>
    /// Throwaway root every artifact this harness creates lives under; deleted
    /// on <see cref="Dispose"/>, so tests can root their own scratch here too.
    /// </summary>
    public string Root => _root;

    public async Task InstallAsync(string packageId, CancellationToken cancellationToken)
    {
        string feed = LocateLocalFeed();
        await _packageService.InstallAsync(new TemplatePackageInstallRequest(packageId, FixtureVersion, feed), cancellationToken);
        await Session.PackageManager.RebuildTemplateCacheAsync(cancellationToken);
    }

    public void UseExtensionBundle(string version)
        => BundleAccessor.Current = new FuncExtensionBundleContext(ExtensionBundleId, version);

    public FuncTemplateCatalog CreateCatalog(
        IFuncTemplateMountFileReader? mountReader = null,
        ILogger<FuncTemplateCatalog>? logger = null)
        => new(
            Session,
            new FuncTemplateConstraintEvaluator(Session),
            mountReader ?? new EngineTemplateMountFileReader(Session),
            logger ?? NullLogger<FuncTemplateCatalog>.Instance);

    public FuncTemplateScaffolder CreateScaffolder(
        IFuncTemplateFileSystem? postActionFileSystem = null,
        IFuncTemplateStagingArea? stagingArea = null)
    {
        IFuncTemplateFileSystem fileSystem = postActionFileSystem ?? new PhysicalFuncTemplateFileSystem();
        IFuncPostActionHandler[] handlers =
        [
            new AppendPostActionHandler(fileSystem),
            new AddReferencePostActionHandler(fileSystem),
            new ManualInstructionsPostActionHandler(),
        ];
        var dispatcher = new FuncPostActionDispatcher(handlers, NullLogger<FuncPostActionDispatcher>.Instance);

        return new FuncTemplateScaffolder(
            Session,
            new FuncTemplateConstraintEvaluator(Session),
            new EngineTemplateMountFileReader(Session),
            dispatcher,
            BundleAccessor,
            ProjectDirectoryAccessor,
            stagingArea ?? new TempFuncTemplateStagingArea(),
            NullLogger<FuncTemplateScaffolder>.Instance);
    }

    public string NewProjectDirectory()
    {
        string directory = Path.Combine(_root, "proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

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
                // Best-effort cleanup: a hive file may still be held after disposal.
            }
        }
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
}
