// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.CommandLine;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-bundled-node-").FullName;
    private readonly PhysicalFileSystem _files = new();
    private readonly IWorkloadPackageSource _source = Substitute.For<IWorkloadPackageSource>();
    private readonly WorkloadPathsOptions _paths;
    private readonly WorkloadStore _workloads;
    private readonly TemplateStore _templates;
    private readonly WorkloadInstaller _installer;
    private readonly LiveInspector _inspector;
    private readonly TestInteractionService _interaction = new();
    private readonly List<Azure.Functions.Cli.Workloads.Loading.WorkloadLoadContext> _loadContexts = [];

    public BundledNodeTemplateTests()
    {
        _paths = new(Path.Combine(_root, "workload-home"));
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        _workloads = new(_paths, coordinator);
        _templates = new(new(Home), _files, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        _inspector = new(_files, Home);
        _installer = CreateInstaller(coordinator);
    }

    private WorkloadInstaller CreateInstaller(IWorkloadOperationCoordinator coordinator, IFileSystem? bundleFiles = null)
    {
        var bundles = new BundledTemplateInstaller(_templates, _inspector, bundleFiles ?? _files, new(Home));
        var metadata = new WorkloadMetadataReader();
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("win-x64");
        var selector = new WorkloadRidPackageSelector(runtime);
        return new(_source, new WorkloadPackageInspector(metadata, selector), selector,
            new WorkloadDeploymentService(_paths, _workloads, metadata, coordinator, _files, bundles),
            coordinator, _workloads, _files);
    }

    private string Home => Path.Combine(_root, "cli-home");
    private static string BuiltArchive => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "bundled-node-artifacts"), "*.nupkg").Single();

    [Fact]
    public async Task NullDependenciesAndInputs_AreRejectedAsProgrammerErrors()
    {
        Action[] constructors =
        [
            () => new BundledTemplateInstaller(null!, _inspector, _files, new(Home)),
            () => new BundledTemplateInstaller(_templates, null!, _files, new(Home)),
            () => new BundledTemplateInstaller(_templates, _inspector, null!, new(Home)),
            () => new BundledTemplateInstaller(_templates, _inspector, _files, null!),
        ];
        foreach (var constructor in constructors)
        {
            constructor.Should().Throw<ArgumentNullException>();
        }
        var installer = new BundledTemplateInstaller(_templates, _inspector, _files, new(Home));
        Func<Task>[] calls =
        [
            () => installer.PrepareAsync(_root, null!, CancellationToken.None),
            () => installer.CommitAsync(null!, _ => Task.FromResult<WorkloadInstallResult>(null!), CancellationToken.None),
            () => installer.ValidateInstalledAsync(null!, CancellationToken.None),
            () => _installer.EnsureInstalledAsync(null!, CancellationToken.None),
        ];
        foreach (var call in calls)
        {
            await call.Should().ThrowAsync<ArgumentNullException>();
        }
    }

    [Fact]
    public void Pack_RealOuterArtifact_ContainsExclusiveNativeArchiveAndOwnedSourceFiles()
    {
        using var outer = new PackageArchiveReader(BuiltArchive);
        outer.NuspecReader.GetPackageTypes().Select(type => type.Name).Should().Contain("FuncCliWorkload").And.NotContain("FuncTemplate");
        using var declaration = JsonDocument.Parse(outer.GetStream("bundled-templates.json"));
        var asset = declaration.RootElement.GetProperty("assets").EnumerateArray().Single();
        asset.GetProperty("path").GetString().Should().Be("tools/any/templates/node-basic.nupkg");
        using var bytes = new MemoryStream();
        outer.GetStream("tools/any/templates/node-basic.nupkg").CopyTo(bytes);
        Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())).Should().Be(asset.GetProperty("sha256").GetString());
        bytes.Position = 0;
        using var inner = new PackageArchiveReader(bytes);
        inner.NuspecReader.GetId().Should().Be("Azure.Functions.Templates.Node.Basic");
        inner.NuspecReader.GetVersion().Should().Be(outer.NuspecReader.GetVersion());
        inner.NuspecReader.GetPackageTypes().Select(type => type.Name).Should().Equal("FuncTemplate");
        inner.GetFiles().Should().Contain("LICENSE", "content/project-js/.funcignore", "content/project-ts/.gitignore");
        asset.GetProperty("templates").EnumerateArray().Select(template => template.GetProperty("identity").GetString())
            .Should().BeEquivalentTo("Azure.Functions.Node.Basic.JavaScript", "Azure.Functions.Node.Basic.TypeScript",
                "Azure.Functions.Node.Http.JavaScript", "Azure.Functions.Node.Http.TypeScript");
        Read(inner, "content/project-js/package.json").Should().Contain("\"main\": \"src/functions/*.js\"", "\"@azure/functions\": \"^4.0.0\"");
        Read(inner, "content/project-ts/package.json").Should().Contain("\"main\": \"dist/src/functions/*.js\"", "\"build\": \"tsc\"");
        Read(inner, "content/http-js/httpTrigger.js").Should().Contain("app.http('httpTrigger'", "return { body: `Hello, ${name}!` };").And.NotContain("function.json");
        Read(inner, "content/http-ts/httpTrigger.ts").Should().Contain("Promise<HttpResponseInit>", "handler: httpTrigger");
    }

    [Fact]
    public async Task Install_ActualArtifactAndCatalogRepeat_ReopensNativeCatalogWithoutAdditionalDownloadOrDanglingMount()
    {
        string archive = Path.Combine(_root, "node.nupkg");
        File.Copy(BuiltArchive, archive);
        var installed = await _installer.InstallFromPackageAsync(archive);
        _source.ResolveAsync(installed.Entry.PackageId, Arg.Any<NuGetVersion>(), Arg.Any<string>(), Arg.Any<bool?>(), true, Arg.Any<CancellationToken>())
            .Returns(new ResolvedPackage(installed.Entry.PackageId, NuGetVersion.Parse(installed.Entry.PackageVersion), new(archive)));
        File.Delete(archive);

        var repeated = await _installer.InstallFromCatalogAsync(installed.Entry.PackageId, NuGetVersion.Parse(installed.Entry.PackageVersion), archive,
            true, true, false);

        repeated.AlreadyInstalled.Should().BeTrue();
        (await _workloads.GetWorkloadsAsync()).Should().ContainSingle();
        using var session = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        var packages = await session.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None);
        packages.Should().ContainSingle();
        File.Exists(packages[0].MountPointUri).Should().BeTrue();
        packages[0].MountPointUri.Should().StartWith(Path.Combine(Home, "templates"));
        using var templater = new Templater(session, new(session, _files));
        (await templater.ListAsync(TemplateType.Project)).Select(template => template.Identity).Should()
            .BeEquivalentTo("Azure.Functions.Node.Basic.JavaScript", "Azure.Functions.Node.Basic.TypeScript");
        _source.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name.Contains("Download", StringComparison.Ordinal));
        File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")).Should().BeFalse();
    }

    [Theory]
    [InlineData("js", "JavaScript", "js", "src/functions/*.js")]
    [InlineData("ts", "TypeScript", "ts", "dist/src/functions/*.js")]
    public async Task StagedCommands_ActualInstalledBundle_PreviewAndCreateProjectThenHttpWithCanonicalConfiguration(
        string alias, string language, string extension, string main)
    {
        var installed = await _installer.InstallFromPackageAsync(BuiltArchive);
        using var provider = await CreateServicesAsync();
        using var scope = provider.CreateScope();
        var availability = new TemplateConstraintEvaluationSnapshot([new(installed.Entry.PackageId, NuGetVersion.Parse(installed.Entry.PackageVersion), ["node"], true)]);
        var init = new InitExecutionCommand(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>(),
            provider.GetRequiredService<InitExecutionRenderer>(), availability);
        string project = Path.Combine(_root, "app-" + alias);
        string[] initArgs = [project, "--template", "basic", "--stack", "NODE", "--language", alias, "--name", "My Sample!"];

        (await init.ParseInvocation([.. initArgs, "--dry-run"]).InvokeAsync()).Should().Be(0, _interaction.AllOutput);
        Directory.Exists(project).Should().BeFalse();
        (await init.ParseInvocation(initArgs).InvokeAsync()).Should().Be(0);

        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, ".func", "config.json")));
        config.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
        config.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be(language);
        using var package = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "package.json")));
        package.RootElement.GetProperty("name").GetString().Should().Be("my-sample");
        package.RootElement.GetProperty("main").GetString().Should().Be(main);
        Directory.Exists(Path.Combine(project, "src", "functions")).Should().BeTrue();
        Directory.GetFiles(Path.Combine(project, "src", "functions")).Should().BeEmpty();
        var command = new NewExecutionCommand(provider.GetRequiredService<INewExecutionRunner>(), provider.GetRequiredService<NewExecutionRenderer>(), availability);
        string target = Path.Combine(project, "src", "functions");
        string[] newArgs = ["http", "--path", target, "--name", "Greeting", "--authlevel", "function"];
        (await command.ParseInvocation([.. newArgs, "--dry-run"]).InvokeAsync()).Should().Be(0);
        File.Exists(Path.Combine(target, "Greeting." + extension)).Should().BeFalse();
        (await command.ParseInvocation(newArgs).InvokeAsync()).Should().Be(0);
        string content = File.ReadAllText(Path.Combine(target, "Greeting." + extension));
        content.Should().Contain("app.http('Greeting'", "authLevel: 'function'", "return { body: `Hello, ${name}!` };");
        Directory.GetFiles(project, "function.json", SearchOption.AllDirectories).Should().BeEmpty();
        File.Exists(Path.Combine(project, ".funcignore")).Should().BeTrue();
        File.Exists(Path.Combine(project, ".gitignore")).Should().BeTrue();
    }

    [Fact]
    public async Task RegistrationFailure_RetainsStackRestoresNativeHiveAndRetriesWithoutStackDownload()
    {
        _inspector.FailLive = true;
        Func<Task> install = () => _installer.InstallFromPackageAsync(BuiltArchive);

        var failed = (await install.Should().ThrowAsync<BundledTemplateRegistrationException>()).Which;
        (await _workloads.GetWorkloadsAsync()).Should().ContainSingle();
        File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")).Should().BeFalse();
        using (var read = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None))
        {
            (await read.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
        }

        _inspector.FailLive = false;
        var retry = await _installer.InstallFromPackageAsync(BuiltArchive);
        retry.AlreadyInstalled.Should().BeTrue();
        failed.Entry.PackageId.Should().Be(retry.Entry.PackageId);
        using var fresh = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await fresh.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().ContainSingle();
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("path")]
    [InlineData("identity")]
    [InlineData("type")]
    [InlineData("inventory")]
    [InlineData("schema")]
    [InlineData("nestedType")]
    [InlineData("nestedConstraints")]
    [InlineData("nestedHost")]
    public async Task InvalidDeclaredAsset_RejectsBeforeStackOrNativeMutation(string corruption)
    {
        string archive = CopyAndChange(corruption);
        Func<Task> install = () => _installer.InstallFromPackageAsync(archive);

        await install.Should().ThrowAsync<InvalidWorkloadException>();
        (await _workloads.GetWorkloadsAsync()).Should().BeEmpty();
        Directory.Exists(_paths.WorkloadsRoot).Should().BeFalse();
        Directory.Exists(Path.Combine(Home, "templates")).Should().BeFalse();
    }

    [Fact]
    public async Task UnknownNativeOwner_EvenIdenticalBytes_RejectsBeforeStackAdmission()
    {
        using var outer = new PackageArchiveReader(BuiltArchive);
        string archive = Path.Combine(_root, "user.nupkg");
        await using (var output = File.Create(archive))
        {
            await outer.GetStream("tools/any/templates/node-basic.nupkg").CopyToAsync(output);
        }
        using (var writer = await _templates.OpenWriteAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None))
        {
            await writer.ExecuteAsync(async (session, token) =>
            {
                var provider = session.PackageManager.GetBuiltInManagedProvider(Microsoft.TemplateEngine.Edge.Settings.InstallationScope.Global);
                (await provider.InstallAsync([new Microsoft.TemplateEngine.Abstractions.Installer.InstallRequest(archive)], token)).Single().Success.Should().BeTrue();
            }, async (session, token) =>
            {
                (await session.PackageManager.GetManagedTemplatePackagesAsync(false, token)).Should().ContainSingle();
            }, CancellationToken.None);
        }

        Func<Task> install = () => _installer.InstallFromPackageAsync(BuiltArchive);

        await install.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*unknown or conflicting*");
        (await _workloads.GetWorkloadsAsync()).Should().BeEmpty();
        using var fresh = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await fresh.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task BundledUninstall_IsExplicitlyUnsupportedAndPreservesNativeAndStackState()
    {
        var installed = await _installer.InstallFromPackageAsync(BuiltArchive);
        Func<Task> remove = () => _installer.UninstallAsync(installed.Entry.PackageId, installed.Entry.PackageVersion);

        await remove.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*not supported*Nothing was removed*");
        (await _workloads.GetWorkloadsAsync()).Should().ContainSingle();
        using var fresh = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await fresh.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().ContainSingle();
    }

    [Theory]
    [InlineData("Stable", "Microsoft.Azure.Functions.ExtensionBundle")]
    [InlineData("Preview", "Microsoft.Azure.Functions.ExtensionBundle.Preview")]
    [InlineData("Experimental", "Microsoft.Azure.Functions.ExtensionBundle.Experimental")]
    [InlineData(null, null)]
    public async Task ProjectParameters_PreserveBundleChannelAndNoBundleSemantics(string? channel, string? expectedId)
    {
        var installed = await _installer.InstallFromPackageAsync(BuiltArchive);
        using var provider = await CreateServicesAsync();
        using var scope = provider.CreateScope();
        var init = new InitExecutionCommand(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>(),
            provider.GetRequiredService<InitExecutionRenderer>(), new([new(installed.Entry.PackageId, NuGetVersion.Parse(installed.Entry.PackageVersion), ["node"], true)]));
        string target = Path.Combine(_root, "parameters");
        string[] options = channel is null ? ["--no-bundles", "true"] : ["--bundles-channel", channel];

        (await init.ParseInvocation([target, "--template", "basic", "--language", "js", .. options]).InvokeAsync())
            .Should().Be(0, _interaction.AllOutput);

        using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "host.json")));
        if (expectedId is null)
        {
            host.RootElement.TryGetProperty("extensionBundle", out _).Should().BeFalse();
        }
        else
        {
            host.RootElement.GetProperty("extensionBundle").GetProperty("id").GetString().Should().Be(expectedId);
            host.RootElement.GetProperty("extensionBundle").GetProperty("version").GetString().Should().Be("[4.*, 5.0.0)");
        }
    }

    [Fact]
    public async Task CancellationBeforePreparation_DoesNotAdmitStackOrTemplates()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Func<Task> install = () => _installer.InstallFromPackageAsync(BuiltArchive, cancellationToken: cancel.Token);

        await install.Should().ThrowAsync<OperationCanceledException>();
        (await _workloads.GetWorkloadsAsync()).Should().BeEmpty();
        Directory.Exists(_paths.WorkloadsRoot).Should().BeFalse();
    }

    [Fact]
    public async Task CancellationWhileWaitingAfterPrivatePreparation_DoesNotMutateEitherStore()
    {
        using var cancel = new CancellationTokenSource();
        using var held = await new FileWorkloadOperationCoordinator(_paths).AcquireAsync(CancellationToken.None);
        var coordinator = new CancelingAdmission(new FileWorkloadOperationCoordinator(_paths), cancel);
        var installer = CreateInstaller(coordinator);
        Func<Task> install = () => installer.InstallFromPackageAsync(BuiltArchive, cancellationToken: cancel.Token);

        await install.Should().ThrowAsync<OperationCanceledException>();
        coordinator.Reached.Should().BeTrue();
        (await _workloads.GetWorkloadsAsync()).Should().BeEmpty();
        Directory.Exists(_paths.WorkloadsRoot).Should().BeFalse();
        Directory.Exists(Path.Combine(Home, "templates")).Should().BeFalse();
    }

    [Fact]
    public async Task CancellationDuringPrivateAssetValidation_DisposesStageWithoutAdmittingStackOrTemplates()
    {
        using var cancel = new CancellationTokenSource();
        _inspector.CancelStage = cancel;
        Func<Task> install = () => _installer.InstallFromPackageAsync(BuiltArchive, cancellationToken: cancel.Token);

        await install.Should().ThrowAsync<OperationCanceledException>();
        (await _workloads.GetWorkloadsAsync()).Should().BeEmpty();
        Directory.Exists(_paths.WorkloadsRoot).Should().BeFalse();
        Directory.Exists(Path.Combine(Home, "templates")).Should().BeFalse();
    }

    [Fact]
    public async Task CancellationDuringNativeValidation_SettlesRecoveryBeforeReturningAndRetainsStackForRetry()
    {
        using var cancel = new CancellationTokenSource();
        _inspector.CancelLive = cancel;
        Func<Task> install = () => _installer.InstallFromPackageAsync(BuiltArchive, cancellationToken: cancel.Token);

        await install.Should().ThrowAsync<OperationCanceledException>().WithMessage("*remains installed*registration was canceled*");
        (await _workloads.GetWorkloadsAsync()).Should().ContainSingle();
        File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")).Should().BeFalse();
        using (var fresh = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None))
        {
            (await fresh.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
        }
        _inspector.CancelLive = null;
        (await _installer.InstallFromPackageAsync(BuiltArchive)).AlreadyInstalled.Should().BeTrue();
    }

    [Fact]
    public async Task ReceiptWriteFailure_RollsBackNativeRegistrationRatherThanLeavingFalseCompletion()
    {
        var fault = Substitute.For<IFileSystem>();
        fault.CreateTempDirectory().Returns(_ => _files.CreateTempDirectory());
        fault.ReadAllTextIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            _files.ReadAllTextIfExistsAsync(call.Arg<string>(), call.Arg<CancellationToken>()));
        fault.When(files => files.CopyFile(Arg.Any<string>(), Arg.Any<string>())).Do(call => _files.CopyFile(call.ArgAt<string>(0), call.ArgAt<string>(1)));
        fault.WriteAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("Owned receipt write failure.")));
        var installer = CreateInstaller(new FileWorkloadOperationCoordinator(_paths), fault);
        Func<Task> install = () => installer.InstallFromPackageAsync(BuiltArchive);

        await install.Should().ThrowAsync<BundledTemplateRegistrationException>().WithMessage("*receipt write failure*");
        (await _workloads.GetWorkloadsAsync()).Should().ContainSingle();
        File.Exists(Path.Combine(Home, ".template-store", "recovery.ready")).Should().BeFalse();
        using var fresh = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await fresh.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task ForceReplacementOfBundledStack_IsUnsupportedBeforePayloadOrNativeMutation()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string registry = File.ReadAllText(_paths.WorkloadRegistryPath);
        Func<Task> replace = () => _installer.InstallFromPackageAsync(BuiltArchive, force: true);

        await replace.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*replacement is not supported*");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(registry);
        using var fresh = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await fresh.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task SetupStyleEnsureAfterPartialInstall_RepairsLocallyWithNoCatalogDownload()
    {
        _inspector.FailLive = true;
        Func<Task> install = () => _installer.InstallFromPackageAsync(BuiltArchive);
        await install.Should().ThrowAsync<BundledTemplateRegistrationException>();
        var entry = (await _workloads.GetWorkloadsAsync()).Single();
        _inspector.FailLive = false;

        Func<Task> check = () => _installer.EnsureInstalledAsync(entry, CancellationToken.None, checkOnly: true);
        await check.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*not registered*");
        using (var beforeRepair = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None))
        {
            (await beforeRepair.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
        }
        var result = await _installer.EnsureInstalledAsync(entry, CancellationToken.None);

        result.AlreadyInstalled.Should().BeTrue();
        _source.ReceivedCalls().Should().BeEmpty();
        using var fresh = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await fresh.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task RemovedDeclaration_DoesNotDowngradeInstalledBundleToLegacySuccess()
    {
        var installed = await _installer.InstallFromPackageAsync(BuiltArchive);
        File.Delete(Path.Combine(_paths.GetInstallDirectory(installed.Entry.PackageId, installed.Entry.PackageVersion), "bundled-templates.json"));
        _installer.HasBundledTemplates(installed.Entry).Should().BeTrue();
        Func<Task> ensure = () => _installer.EnsureInstalledAsync(installed.Entry, CancellationToken.None);

        await ensure.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*manifest changed*");
        (await _workloads.GetWorkloadsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task UndeclaredArchive_RemainsLegacyAndDoesNotAutoRegisterNestedArchives()
    {
        string archive = Path.Combine(_root, "legacy.nupkg");
        File.Copy(BuiltArchive, archive);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            zip.GetEntry("bundled-templates.json")!.Delete();
        }

        var result = await _installer.InstallFromPackageAsync(archive);

        _installer.HasBundledTemplates(result.Entry).Should().BeFalse();
        using var fresh = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await fresh.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
        (await _installer.UninstallAsync(result.Entry.PackageId, result.Entry.PackageVersion)).Should().BeTrue();
    }

    [Fact]
    public async Task UndeclaredSameVersion_CannotCompleteRetainedBundledStackWithMissingNativeRegistration()
    {
        _inspector.FailLive = true;
        Func<Task> first = () => _installer.InstallFromPackageAsync(BuiltArchive);
        await first.Should().ThrowAsync<BundledTemplateRegistrationException>();
        _inspector.FailLive = false;
        string incoming = CopyUndeclared("undeclared-same.nupkg");
        string before = File.ReadAllText(_paths.WorkloadRegistryPath);
        Func<Task> repeat = () => _installer.InstallFromPackageAsync(incoming);

        await repeat.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*undeclared artifact cannot complete*");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(before);
        using var native = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await native.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task UndeclaredOlderVersion_CannotEnterSideBySideWithBundledIncumbent()
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string incoming = CopyUndeclared("undeclared-older.nupkg", "0.1.0");
        Func<Task> install = () => _installer.InstallFromPackageAsync(incoming);

        await install.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*Side-by-side*not supported*");
        (await _workloads.GetWorkloadsAsync()).Should().ContainSingle().Which.BundledTemplatesDeclared.Should().BeTrue();
    }

    [Fact]
    public async Task UndeclaredUpdate_CannotOverwriteBundledDestinationFromRetainedLegacyRow()
    {
        var bundled = await _installer.InstallFromPackageAsync(BuiltArchive);
        var legacy = new WorkloadEntry { PackageId = bundled.Entry.PackageId, PackageVersion = "0.1.0", EntryPoint = bundled.Entry.EntryPoint,
            Aliases = ["node"], Source = Path.Combine(_root, "legacy.nupkg") };
        await _workloads.SaveWorkloadAsync(legacy);
        string incoming = CopyUndeclared("undeclared-update.nupkg");
        var metadata = new WorkloadMetadataReader();
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("win-x64");
        var inspected = await new WorkloadPackageInspector(metadata, new(runtime)).InspectAsync(incoming);
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var deployment = new WorkloadDeploymentService(_paths, _workloads, metadata, coordinator, _files,
            new BundledTemplateInstaller(_templates, _inspector, _files, new(Home)));
        using var prepared = await deployment.PrepareAsync(inspected, incoming, WorkloadOwnershipKind.Explicit, null, true, null, CancellationToken.None);
        using var operation = await coordinator.AcquireAsync(CancellationToken.None);
        string before = File.ReadAllText(_paths.WorkloadRegistryPath);
        Func<Task> update = () => deployment.UpdatePreparedAsync(operation, legacy, prepared, CancellationToken.None);

        await update.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*replacement is not supported*");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(before);
        File.Exists(Path.Combine(_paths.GetInstallDirectory(bundled.Entry.PackageId, bundled.Entry.PackageVersion), "bundled-templates.json")).Should().BeTrue();
        using var native = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await native.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task LogicalReuse_CannotAttachNewOwnerToBundledDestination()
    {
        var bundled = await _installer.InstallFromPackageAsync(BuiltArchive);
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var metadata = Substitute.For<IWorkloadMetadataReader>();
        metadata.Read(Arg.Any<string>()).Returns(new WorkloadMetadata { Schema = WorkloadManifestSchema.PackageManifestV1Schema,
            RuntimeIdentifier = "win-x64", EntryPoint = bundled.Entry.EntryPoint });
        var ridEntry = new WorkloadEntry { PackageId = bundled.Entry.PackageId, PackageVersion = bundled.Entry.PackageVersion, EntryPoint = bundled.Entry.EntryPoint,
            Source = bundled.Entry.Source, RuntimeIdentifier = "win-x64", BundledTemplatesDeclared = true };
        await _workloads.SaveWorkloadAsync(ridEntry);
        var deployment = new WorkloadDeploymentService(_paths, _workloads, metadata, coordinator, _files,
            new BundledTemplateInstaller(_templates, _inspector, _files, new(Home)));
        string before = File.ReadAllText(_paths.WorkloadRegistryPath);
        Func<Task> attach = () => deployment.TryReuseImplementationAsync(ridEntry.PackageId, ridEntry.PackageVersion, "win-x64",
            new LogicalPackage { PackageId = "New.Pointer", PackageVersion = ridEntry.PackageVersion, Source = ridEntry.Source });

        await attach.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*new logical owner*not supported*");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(before);
    }

    [Fact]
    public async Task LogicalInstallRefusal_DoesNotRepairNativeStateDuringReuseValidation()
    {
        _inspector.FailLive = true;
        Func<Task> first = () => _installer.InstallFromPackageAsync(BuiltArchive);
        await first.Should().ThrowAsync<BundledTemplateRegistrationException>();
        _inspector.FailLive = false;
        var bundled = (await _workloads.GetWorkloadsAsync()).Single();
        var ridEntry = new WorkloadEntry { PackageId = bundled.PackageId, PackageVersion = bundled.PackageVersion, EntryPoint = bundled.EntryPoint,
            Source = bundled.Source, RuntimeIdentifier = "win-x64", BundledTemplatesDeclared = true };
        string directory = _paths.GetInstallDirectory(ridEntry.PackageId, ridEntry.PackageVersion);
        _files.CopyDirectory(Path.Combine(directory, "tools", "any"), Path.Combine(directory, "tools", "win-x64"));
        await _workloads.SaveWorkloadAsync(ridEntry);
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var metadata = Substitute.For<IWorkloadMetadataReader>();
        metadata.Read(Arg.Any<string>()).Returns(new WorkloadMetadata { Schema = WorkloadManifestSchema.PackageManifestV1Schema,
            RuntimeIdentifier = "win-x64", EntryPoint = ridEntry.EntryPoint });
        var deployment = new WorkloadDeploymentService(_paths, _workloads, metadata, coordinator, _files,
            new BundledTemplateInstaller(_templates, _inspector, _files, new(Home)));
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("win-x64");
        var selector = new WorkloadRidPackageSelector(runtime);
        var installer = new WorkloadInstaller(_source, new WorkloadPackageInspector(metadata, selector), selector, deployment, coordinator, _workloads, _files);
        var logical = new LogicalPackage { PackageId = "New.Pointer", PackageVersion = ridEntry.PackageVersion, Source = ridEntry.Source };
        var probe = new PreparedWorkloadInstall(ridEntry.PackageId, ridEntry.PackageVersion, ridEntry.Source, null, null, "win-x64", logical, null, "");
        using var plan = new PreparedWorkloadInstall(ridEntry.PackageId, ridEntry.PackageVersion, ridEntry.Source, null, null, "win-x64", logical,
            null, probe.CaptureState(await _workloads.GetWorkloadsAsync())) { Reuse = await deployment.PrepareReuseAsync(ridEntry, CancellationToken.None) };
        using var operation = await coordinator.AcquireAsync(CancellationToken.None);
        string before = File.ReadAllText(_paths.WorkloadRegistryPath);
        Func<Task> commit = () => installer.CommitPreparedAsync(operation, plan, null, CancellationToken.None);

        await commit.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*new logical owner*not supported*");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(before);
        using var native = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await native.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
        Directory.Exists(Path.Combine(Home, "templates", ".bundled-owners")).Should().BeFalse();
    }

    [Fact]
    public async Task UndeclaredUpdate_CannotCreateNewVersionBesideBundledSibling()
    {
        var bundled = await _installer.InstallFromPackageAsync(BuiltArchive);
        var legacy = new WorkloadEntry { PackageId = bundled.Entry.PackageId, PackageVersion = "0.1.0", EntryPoint = bundled.Entry.EntryPoint,
            Aliases = ["node"], Source = Path.Combine(_root, "legacy.nupkg") };
        await _workloads.SaveWorkloadAsync(legacy);
        string incoming = CopyUndeclared("undeclared-update-sibling.nupkg", "2.0.0");
        var metadata = new WorkloadMetadataReader();
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("win-x64");
        var inspected = await new WorkloadPackageInspector(metadata, new(runtime)).InspectAsync(incoming);
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var deployment = new WorkloadDeploymentService(_paths, _workloads, metadata, coordinator, _files,
            new BundledTemplateInstaller(_templates, _inspector, _files, new(Home)));
        using var prepared = await deployment.PrepareAsync(inspected, incoming, WorkloadOwnershipKind.Explicit, null, true, null, CancellationToken.None);
        using var operation = await coordinator.AcquireAsync(CancellationToken.None);
        string before = File.ReadAllText(_paths.WorkloadRegistryPath);
        Func<Task> update = () => deployment.UpdatePreparedAsync(operation, legacy, prepared, CancellationToken.None);

        await update.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*bundled sibling*not supported*");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(before);
        Directory.Exists(_paths.GetInstallDirectory(legacy.PackageId, "2.0.0")).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SetupResolvedSourceConflict_DoesNotRepairRetainedPartialBundle(int policyValue)
    {
        _inspector.FailLive = true;
        Func<Task> first = () => _installer.InstallFromPackageAsync(BuiltArchive);
        await first.Should().ThrowAsync<BundledTemplateRegistrationException>();
        _inspector.FailLive = false;
        var entry = (await _workloads.GetWorkloadsAsync()).Single();
        const string requestedSource = "https://different-source.invalid/v3/index.json";
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.ResolveLatestVersionAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<NuGetVersion?>(), true,
                requestedSource, Arg.Any<CancellationToken>())
            .Returns(new ResolvedPackage(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), new(requestedSource)));
        var setup = new SetupDependencyInstaller(_interaction, _workloads, catalog, _installer);
        var options = new SetupCommandOptions(new(_root), ["node"], [], requestedSource, (SetupInstallPolicy)policyValue, true, true, true, false, SetupOutputMode.Json);
        string before = File.ReadAllText(_paths.WorkloadRegistryPath);

        var result = await setup.EnsureDependencyAsync(options, SetupDependency.Stack("node"), CancellationToken.None);

        result.Status.Should().Be(SetupDependencyStatus.Failed);
        result.Message.Should().Contain("requested source conflicts");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(before);
        using var native = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await native.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoOpUpdate_RetainedPartialBundle_CannotSucceedOrRepair(bool exactVersionResolved)
    {
        _inspector.FailLive = true;
        Func<Task> first = () => _installer.InstallFromPackageAsync(BuiltArchive);
        await first.Should().ThrowAsync<BundledTemplateRegistrationException>();
        _inspector.FailLive = false;
        var entry = (await _workloads.GetWorkloadsAsync()).Single();
        _source.ResolveLatestVersionAsync(entry.PackageId, true, Arg.Any<NuGetVersion>(), false, entry.Source, Arg.Any<CancellationToken>())
            .Returns(exactVersionResolved ? new ResolvedPackage(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), new(entry.Source)) : null);
        string before = File.ReadAllText(_paths.WorkloadRegistryPath);
        Func<Task> update = () => _installer.UpdateAsync(entry.PackageId, null, entry.Source, true, false);

        await update.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*not registered*");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(before);
        using var native = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await native.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
        Directory.Exists(Path.Combine(Home, "templates", ".bundled-owners")).Should().BeFalse();
    }

    [Fact]
    public async Task NoOpUpdate_CompleteExactOriginBundle_VerifiesWithoutChangingReceipt()
    {
        var result = await _installer.InstallFromPackageAsync(BuiltArchive);
        var entry = result.Entry;
        _source.ResolveLatestVersionAsync(entry.PackageId, true, Arg.Any<NuGetVersion>(), false, entry.Source, Arg.Any<CancellationToken>())
            .Returns(new ResolvedPackage(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), new(entry.Source)));
        string ownerPath = Path.Combine(Home, "templates", ".bundled-owners", "azure.functions.templates.node.basic.json");
        string before = File.ReadAllText(ownerPath);

        var update = await _installer.UpdateAsync(entry.PackageId, null, entry.Source, true, false);

        update.NoUpdateAvailable.Should().BeTrue();
        File.ReadAllText(ownerPath).Should().Be(before);
        _source.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name.Contains("Download", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublicNoOpUpdate_PartialBundle_DoesNotRenderSuccess()
    {
        _inspector.FailLive = true;
        Func<Task> first = () => _installer.InstallFromPackageAsync(BuiltArchive);
        await first.Should().ThrowAsync<BundledTemplateRegistrationException>();
        _inspector.FailLive = false;
        var entry = (await _workloads.GetWorkloadsAsync()).Single();
        _source.ResolveLatestVersionAsync(entry.PackageId, true, Arg.Any<NuGetVersion>(), false, entry.Source, Arg.Any<CancellationToken>())
            .Returns(new ResolvedPackage(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), new(entry.Source)));
        var command = new WorkloadUpdateCommand(_interaction, _installer, _workloads, Options.Create(new WorkloadCatalogOptions()));
        RootCommand root = [command];
        Func<Task> invoke = () => root.Parse([command.Name, entry.PackageId, "--exact", "--prerelease", "--source", entry.Source])
            .InvokeAsync(new() { EnableDefaultExceptionHandler = false });

        await invoke.Should().ThrowAsync<GracefulException>().WithMessage("*not registered*");
        _interaction.AllOutput.Should().NotContain("latest version").And.NotContain("Updated workload").And.NotContain("up to date");
        using var native = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await native.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task CachedLogicalUpdate_CannotMoveOwnershipBesideBundledSibling()
    {
        var bundled = await _installer.InstallFromPackageAsync(BuiltArchive);
        var oldLogical = new LogicalPackage { PackageId = "Historical.Pointer", PackageVersion = "0.1.0", Source = bundled.Entry.Source };
        var legacy = new WorkloadEntry { PackageId = bundled.Entry.PackageId, PackageVersion = "0.1.0",
            RuntimeIdentifier = "win-x64", EntryPoint = bundled.Entry.EntryPoint, IsImplicitlyInstalled = true, LogicalPackage = oldLogical, Source = bundled.Entry.Source };
        var cached = new WorkloadEntry { PackageId = bundled.Entry.PackageId, PackageVersion = "2.0.0",
            RuntimeIdentifier = "win-x64", EntryPoint = bundled.Entry.EntryPoint, Source = bundled.Entry.Source };
        await _workloads.SaveWorkloadAsync(legacy);
        await _workloads.SaveWorkloadAsync(cached);
        string cachedDirectory = _paths.GetInstallDirectory(cached.PackageId, cached.PackageVersion);
        _files.CopyDirectory(_paths.GetInstallDirectory(bundled.Entry.PackageId, bundled.Entry.PackageVersion), cachedDirectory);
        _files.CopyDirectory(Path.Combine(cachedDirectory, "tools", "any"), Path.Combine(cachedDirectory, "tools", "win-x64"));
        File.Delete(Path.Combine(cachedDirectory, "bundled-templates.json"));
        File.Delete(Path.Combine(cachedDirectory, ".bundled-template-origin.json"));
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var metadata = Substitute.For<IWorkloadMetadataReader>();
        metadata.Read(Arg.Any<string>()).Returns(new WorkloadMetadata { Schema = WorkloadManifestSchema.PackageManifestV1Schema,
            RuntimeIdentifier = "win-x64", EntryPoint = cached.EntryPoint });
        var deployment = new WorkloadDeploymentService(_paths, _workloads, metadata, coordinator, _files,
            new BundledTemplateInstaller(_templates, _inspector, _files, new(Home)));
        using var prepared = await deployment.PrepareReuseAsync(cached, CancellationToken.None);
        using var operation = await coordinator.AcquireAsync(CancellationToken.None);
        await deployment.ValidateReuseAsync(operation, prepared, CancellationToken.None);
        string before = File.ReadAllText(_paths.WorkloadRegistryPath);
        Func<Task> update = () => deployment.TryReuseImplementationForUpdateAsync(operation, legacy, cached.PackageId, cached.PackageVersion, "win-x64",
            new LogicalPackage { PackageId = oldLogical.PackageId, PackageVersion = cached.PackageVersion, Source = oldLogical.Source }, CancellationToken.None);

        await update.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*bundled sibling*not supported*");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(before);
        (await _workloads.GetWorkloadsAsync()).Single(row => row.PackageVersion == "0.1.0").LogicalPackage.Should().BeEquivalentTo(oldLogical);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogReuse_ConflictingRequestedSource_CannotRepairOrCompleteFromStoredOrigin(bool partial)
    {
        _inspector.FailLive = partial;
        if (partial)
        {
            Func<Task> first = () => _installer.InstallFromPackageAsync(BuiltArchive);
            await first.Should().ThrowAsync<BundledTemplateRegistrationException>();
        }
        else
        {
            await _installer.InstallFromPackageAsync(BuiltArchive);
        }
        _inspector.FailLive = false;
        var installed = (await _workloads.GetWorkloadsAsync()).Single();
        const string requestedSource = "https://different-source.invalid/v3/index.json";
        _source.ResolveAsync(installed.PackageId, Arg.Any<NuGetVersion>(), requestedSource, Arg.Any<bool?>(), true, Arg.Any<CancellationToken>())
            .Returns(new ResolvedPackage(installed.PackageId, NuGetVersion.Parse(installed.PackageVersion), new(requestedSource)));
        string before = File.ReadAllText(_paths.WorkloadRegistryPath);
        Func<Task> repeat = () => _installer.InstallFromCatalogAsync(installed.PackageId, NuGetVersion.Parse(installed.PackageVersion), requestedSource, true, true, false);

        await repeat.Should().ThrowAsync<InvalidWorkloadException>().WithMessage("*requested source conflicts*");
        File.ReadAllText(_paths.WorkloadRegistryPath).Should().Be(before);
        _source.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name.Contains("Download", StringComparison.Ordinal));
        using var native = await _templates.OpenReadAsync(new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);
        (await native.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None)).Count.Should().Be(partial ? 0 : 1);
    }

    private string CopyUndeclared(string name, string? version = null)
    {
        string archive = Path.Combine(_root, name);
        File.Copy(BuiltArchive, archive);
        using var zip = ZipFile.Open(archive, ZipArchiveMode.Update);
        zip.GetEntry("bundled-templates.json")!.Delete();
        if (version is not null)
        {
            var entry = zip.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
            System.Xml.Linq.XDocument document;
            using (var input = entry.Open())
            {
                document = System.Xml.Linq.XDocument.Load(input);
            }
            var ns = document.Root!.Name.Namespace;
            document.Root.Element(ns + "metadata")!.Element(ns + "version")!.Value = version;
            string path = entry.FullName;
            entry.Delete();
            using var output = zip.CreateEntry(path).Open();
            document.Save(output);
        }
        return archive;
    }

    private async Task<ServiceProvider> CreateServicesAsync()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IInteractionService>(_interaction);
        services.AddSingleton<IFileSystem>(_files);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(new CliConfigurationPathsOptions(Home));
        services.AddSingleton<ILocalSettingsProvider, LocalSettingsProvider>();
        services.AddSingleton<ICliConfigurationProvider, CliConfigurationProvider>();
        services.AddOptions();
        services.AddSingleton<IConfigureOptions<StackOptions>, StackOptionsSetup>();
        services.AddSingleton<IConfigureOptions<ProjectProfileOptions>, ProjectProfileOptionsSetup>();
        services.AddSingleton<ProfileDocumentParser>();
        services.AddSingleton<IProfileSource, BuiltInProfileSource>();
        services.AddSingleton<IProfileCatalog, ProfileCatalog>();
        services.AddSingleton<IProfileResolver, ProfileResolver>();
        await WorkloadRegistration.RegisterWorkloadsAsync(services, _paths, _interaction, CancellationToken.None);
        _loadContexts.AddRange(services.Select(service => service.ImplementationInstance).OfType<RuntimeWorkloadInfo>().Select(workload => workload.LoadContext));
        services.AddSingleton<IFunctionsProjectResolver, FunctionsProjectResolver>();
        services.AddSingleton<IHostJsonBundleSectionReader, HostJsonBundleSectionReader>();
        var bundles = Substitute.For<IInstalledBundleWorkloads>();
        bundles.ListInstalledAsync(Arg.Any<CancellationToken>()).Returns([new InstalledBundleWorkload("4.35.0", Path.Combine(_root, "bundle"))]);
        services.AddSingleton<IExtensionBundleResolver>(new ExtensionBundleResolver(new InstalledBundleScanner(bundles),
            NullBundleResolveTelemetry.Instance, NullLogger<ExtensionBundleResolver>.Instance));
        services.AddTemplatesOrchestrator();
        services.AddSingleton<InstalledProjectStackCatalog>();
        services.AddSingleton<TemplateProjectSupport>();
        services.AddSingleton<ITemplateStore>(_templates);
        return services.BuildServiceProvider();
    }

    private string CopyAndChange(string corruption)
    {
        string archive = Path.Combine(_root, "invalid.nupkg");
        File.Copy(BuiltArchive, archive);
        using var zip = ZipFile.Open(archive, ZipArchiveMode.Update);
        var entry = zip.GetEntry("bundled-templates.json")!;
        JsonNode root;
        using (var reader = new StreamReader(entry.Open()))
        {
            root = JsonNode.Parse(reader.ReadToEnd())!;
        }
        var asset = root["assets"]![0]!;
        if (corruption.StartsWith("nested", StringComparison.Ordinal))
        {
            var nested = zip.GetEntry("tools/any/templates/node-basic.nupkg")!;
            using var bytes = new MemoryStream();
            using (var payload = nested.Open())
            {
                payload.CopyTo(bytes);
            }
            using (var inner = new ZipArchive(bytes, ZipArchiveMode.Update, leaveOpen: true))
            {
                string path = corruption == "nestedType" ? "Azure.Functions.Templates.Node.Basic.nuspec"
                    : corruption == "nestedHost" ? "content/http-js/.template.config/func.host.json"
                    : "content/http-js/.template.config/template.json";
                var content = inner.GetEntry(path)!;
                string text;
                using (var reader = new StreamReader(content.Open()))
                {
                    text = reader.ReadToEnd();
                }
                text = corruption == "nestedType" ? text.Replace("FuncTemplate", "FuncCliWorkload", StringComparison.Ordinal)
                    : corruption == "nestedHost" ? """{"symbolInfo":42}"""
                    : text.Replace("\"args\": \"node\"", "\"args\": 42", StringComparison.Ordinal);
                content.Delete();
                using var output = new StreamWriter(inner.CreateEntry(path).Open());
                output.Write(text);
            }
            nested.Delete();
            using (var output = zip.CreateEntry("tools/any/templates/node-basic.nupkg").Open())
            {
                output.Write(bytes.ToArray());
            }
            asset["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
        }
        switch (corruption)
        {
            case "digest": asset["sha256"] = new string('0', 64); break;
            case "path": asset["path"] = "tools/../../outside.nupkg"; break;
            case "identity": asset["packageId"] = "Another.Package"; break;
            case "type": asset["templates"]![0]!["type"] = "project"; break;
            case "inventory": asset["templates"]!.AsArray().RemoveAt(0); break;
            case "schema": root["schemaVersion"] = 2; break;
        }
        entry.Delete();
        using var writer = new StreamWriter(zip.CreateEntry("bundled-templates.json").Open());
        writer.Write(root.ToJsonString());
        return archive;
    }

    private static string Read(PackageArchiveReader archive, string path)
    {
        using var reader = new StreamReader(archive.GetStream(path));
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        foreach (var context in _loadContexts)
        {
            context.Unload();
        }

        try
        {
            Directory.Delete(_root, true);
        }
        catch (UnauthorizedAccessException) when (_loadContexts.Count > 0)
        {
            // Windows retains loaded workload DLLs until the test process exits; the test owns this temporary directory only.
        }
    }

    private sealed class LiveInspector(IFileSystem files, string home) : IBasicArtifactInspector
    {
        private readonly BasicArtifactInspector _inner = new(files);
        public bool FailLive { get; set; }
        public CancellationTokenSource? CancelLive { get; set; }
        public CancellationTokenSource? CancelStage { get; set; }
        public async Task ValidateAsync(string archive, BasicTemplateArtifact artifact, CancellationToken cancellationToken)
        {
            await _inner.ValidateAsync(archive, artifact, cancellationToken);
            if (CancelStage is not null)
            {
                CancelStage.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (archive.StartsWith(Path.Combine(home, "templates"), StringComparison.Ordinal) && CancelLive is not null)
            {
                CancelLive.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (FailLive && archive.StartsWith(Path.Combine(home, "templates"), StringComparison.Ordinal))
            {
                throw new BasicAcquisitionException(BasicAcquisitionFailure.InstallFailed, "Owned live-validation failure.");
            }
        }

    }

    private sealed class CancelingAdmission(IWorkloadOperationCoordinator inner, CancellationTokenSource cancel) : IWorkloadOperationCoordinator
    {
        public bool Reached { get; private set; }
        public Task<IWorkloadOperation> AcquireAsync(CancellationToken cancellationToken, Action? waiting = null)
        {
            return inner.AcquireAsync(cancellationToken, () =>
            {
                Reached = true;
                cancel.Cancel();
                waiting?.Invoke();
            });
        }
    }
}
