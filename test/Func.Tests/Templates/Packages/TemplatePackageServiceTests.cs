// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Packages;
using Microsoft.Extensions.Options;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using NSubstitute;
using NuGet.Packaging.Signing;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates.Packages;

public sealed class TemplatePackageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Directory.GetCurrentDirectory(), "template-packages-" + Guid.NewGuid().ToString("N"));
    private readonly ArchiveCatalog _catalog;
    private readonly TestFactory _factory;
    private readonly TemplatePackageService _service;
    private readonly TemplatePackageVerifier _verifier;
    private readonly IWorkloadTemplateSource _workloads = Substitute.For<IWorkloadTemplateSource>();
    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
    private readonly List<string> _temporaryDirectories = [];

    private readonly ITestOutputHelper _output;

    public TemplatePackageServiceTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        Directory.CreateDirectory(_root);
        _catalog = new ArchiveCatalog(Path.Combine(_root, "fixture.nupkg"));
        _factory = new TestFactory(Path.Combine(_root, "templates"));
        _workloads.GetRootsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var fileSystem = new PhysicalFileSystem();
        _fileSystem.CreateTempDirectory().Returns(_ =>
        {
            var directory = fileSystem.CreateTempDirectory(_root);
            _temporaryDirectories.Add(directory.Path);
            return directory;
        });
        _fileSystem.OpenReadIfExists(Arg.Any<string>()).Returns(c => fileSystem.OpenReadIfExists(c.Arg<string>()));
        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(c => fileSystem.DirectoryExists(c.Arg<string>()));
        _fileSystem.SaveStreamToFileAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(c => fileSystem.SaveStreamToFileAsync(c.ArgAt<string>(0), c.ArgAt<Stream>(1), c.ArgAt<CancellationToken>(2)));
        _fileSystem.When(f => f.CopyFile(Arg.Any<string>(), Arg.Any<string>()))
            .Do(c => fileSystem.CopyFile(c.ArgAt<string>(0), c.ArgAt<string>(1)));
        _verifier = new TemplatePackageVerifier();
        _service = new TemplatePackageService(_catalog, new TemplatePackageValidator(), _factory,
            _fileSystem, _workloads, _verifier);
        _catalog.Archive = MakeArchive();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Verifier_NullArchiveIsRejected()
    {
        Func<Task> act = () => _verifier.VerifyAsync(null!, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("archive");
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("validator")]
    [InlineData("factory")]
    [InlineData("fileSystem")]
    [InlineData("workloadTemplates")]
    [InlineData("verifier")]
    public void Constructor_NullDependenciesAreRejected(string parameter)
    {
        Action act = () => _ = new TemplatePackageService(
            parameter == "catalog" ? null! : _catalog,
            parameter == "validator" ? null! : new TemplatePackageValidator(),
            parameter == "factory" ? null! : _factory,
            parameter == "fileSystem" ? null! : _fileSystem,
            parameter == "workloadTemplates" ? null! : _workloads,
            parameter == "verifier" ? null! : _verifier);

        act.Should().Throw<ArgumentNullException>().WithParameterName(parameter);
    }

    [Fact]
    public async Task Install_PreflightFailureDoesNotCreateOrMutateHive()
    {
        _catalog.Archive = MakeArchive(includeTemplates: false);

        Func<Task> act = () => InstallAsync("1.0.0");

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*at least one template identity*");
        Directory.Exists(_factory.HivePath).Should().BeFalse();
        _factory.Installations.Should().Be(0);
        AssertTemporaryFilesRemoved();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Install_SignedAndUnsignedPackagesUseInProcessIntegrityVerification(bool signed)
    {
        File.WriteAllText(Path.Combine(_root, "NuGet.Config"), """
            <configuration>
              <config><add key="signatureValidationMode" value="require" /></config>
              <trustedSigners><clear /><author name="unrelated">
                <certificate fingerprint="0000000000000000000000000000000000000000000000000000000000000000"
                  hashAlgorithm="SHA256" allowUntrustedRoot="false" />
              </author></trustedSigners>
            </configuration>
            """);
        if (signed)
        {
            _catalog.Archive = await SignArchiveAsync(_catalog.Archive);
        }

        await InstallAsync("1.0.0");

        await AssertInstalledVersionAsync("1.0.0");
        AssertTemporaryFilesRemoved();
    }

    [Fact]
    public async Task Install_TamperedSignedArchiveLeavesInstallationUntouchedAndCleansDownload()
    {
        await InstallAsync("1.0.0");
        int installations = _factory.Installations;
        byte[] archive = await SignArchiveAsync(MakeArchive("2.0.0"));
        using var stream = new MemoryStream();
        stream.Write(archive);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("tampered.txt").Open());
            writer.Write("changed after signing");
        }

        _catalog.Archive = stream.ToArray();
        Func<Task> act = () => InstallAsync("2.0.0");

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*integrity*");
        AssertTemporaryFilesRemoved();
        _factory.Installations.Should().Be(installations);
        _factory.Uninstallations.Should().Be(0);
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CliInstall_SignedPackageFromExplicitSourceOwnsItsArchive()
    {
        byte[] archive = await SignArchiveAsync(_catalog.Archive);
        await using LoopbackTemplateFeed feed = new();
        feed.Add("1.0.0", archive);
        string home = Path.Combine(_root, "signed-home");

        var result = await RunCliAsync(home, ["templates", "install", "test.templates", "--source", feed.Source, "--version", "1.0.0"]);

        result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        await AssertCliPackageAsync(home, "1.0.0", archive);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CliInstall_UsesConfiguredFeedAndRestoresPreviousPackageWhenReplacementFails()
    {
        await using LoopbackTemplateFeed feed = new();
        byte[] stable = _catalog.Archive;
        byte[] prerelease = MakeArchive("3.0.0-preview.1");
        feed.Add("1.0.0", stable);
        feed.Add("3.0.0-preview.1", prerelease);
        File.WriteAllText(Path.Combine(_root, "NuGet.Config"), """
            <configuration>
              <packageSources><clear /><add key="ignored" value="http://127.0.0.1:1/v3/index.json" /></packageSources>
              <config><add key="signatureValidationMode" value="require" /></config>
            </configuration>
            """);
        string home = Path.Combine(_root, "cli-home");
        Dictionary<string, string> environment = new() { ["FUNC_CLI_TEMPLATES_SOURCE"] = feed.Source };

        (await RunCliAsync(home, ["templates", "install", "test.templates"], environment)).ExitCode.Should().Be(0);
        await AssertCliPackageAsync(home, "1.0.0", stable);
        (await RunCliAsync(home, ["templates", "install", "test.templates", "--prerelease"], environment)).ExitCode.Should().Be(0);
        await AssertCliPackageAsync(home, "3.0.0-preview.1", prerelease);
        (await RunCliAsync(home, ["templates", "install", "test.templates", "--version", "1.0.0"], environment)).ExitCode.Should().Be(0);
        feed.Add("2.0.0", MakeArchive("2.0.0", invalidTemplate: true));

        var failed = await RunCliAsync(home, ["templates", "install", "test.templates", "--version", "2.0.0"], environment);

        failed.ExitCode.Should().Be(1, failed.StandardOutput + failed.StandardError);
        await AssertCliPackageAsync(home, "1.0.0", stable);
    }

    private Task<CliProcessTestRunner.CliResult> RunCliAsync(
        string home, string[] args, IReadOnlyDictionary<string, string>? environment = null)
        => new CliProcessTestRunner(_output).RunAsync(
            _root, home, Path.Combine(_root, "workload-home"), _root, args, TestContext.Current.CancellationToken, environment);

    private async Task AssertCliPackageAsync(string home, string version, byte[] archive)
    {
        var factory = new TemplaterFactory(new CliConfigurationPathsOptions(home),
            new FuncTemplateComponentsProvider([], [], Substitute.For<IInteractionService>()));
        using Templater templater = factory.Create(Context());
        var package = (await templater.GetManagedPackagesAsync(TestContext.Current.CancellationToken)).Should().ContainSingle().Subject;
        package.Version.Should().Be(version);
        package.IsLocalPackage.Should().BeTrue();
        package.GetDetails().Should().NotContainKey("NuGetSource");
        package.MountPointUri.Should().StartWith(factory.HivePath);
        File.ReadAllBytes(package.MountPointUri).Should().Equal(archive);
        (await templater.ListAsync(TestContext.Current.CancellationToken)).Templates.Should().HaveCount(4);
    }

    [Fact]
    public async Task InstallAndUninstall_AreVisibleToFreshDiscoveryAndUseManagedNuGetIdentity()
    {
        await InstallAsync("1.0.0");

        using (var templater = _factory.Create(Context()))
        {
            (await templater.ListAsync(TestContext.Current.CancellationToken)).Templates.Should().HaveCount(4);
            var installed = (await templater.GetManagedPackagesAsync(TestContext.Current.CancellationToken)).Should().ContainSingle().Subject;
            installed.Identifier.Should().Be("test.templates");
            _factory.Request!.PackageIdentifier.Should().EndWith("package.nupkg");
            File.Exists(_factory.Request.PackageIdentifier).Should().BeFalse("the download is cleaned up after installation");
            installed.MountPointUri.Should().StartWith(_factory.HivePath).And.NotBe(_factory.Request.PackageIdentifier);
            File.ReadAllBytes(installed.MountPointUri).Should().Equal(_catalog.Archive);
            _factory.Request.Details.Should().NotContainKey("NuGetSources");
            _factory.Request.Force.Should().BeFalse();
        }

        await _service.UninstallAsync("TEST.TEMPLATES", TestContext.Current.CancellationToken);

        using var after = _factory.Create(Context());
        (await after.ListAsync(TestContext.Current.CancellationToken)).Templates.Should().BeEmpty();
    }

    [Fact]
    public async Task Install_SameVersionIsNoOpRegardlessOfSource()
    {
        await InstallAsync("1.0.0");
        int installations = _factory.Installations;

        bool changed = await InstallAsync("1.0.0");
        bool sourceChange = await _service.InstallAsync("test.templates", "1.0.0", "https://other.test/index.json", false, TestContext.Current.CancellationToken);

        changed.Should().BeFalse();
        sourceChange.Should().BeFalse();
        _factory.Installations.Should().Be(installations);
    }

    [Fact]
    public async Task Install_PassesOnlyTheStagedArchiveToTheEngine()
    {
        await _service.InstallAsync("test.templates", "1.0.0", null, false, TestContext.Current.CancellationToken);

        _factory.Request!.Details.Should().NotContainKey("NuGetSources");
        _factory.Request.Version.Should().BeNull();
        _factory.Request.Force.Should().BeFalse();
    }

    [Fact]
    public async Task Install_ReplacesAndAllowsExplicitDowngradeUsingManagedArchiveStorage()
    {
        await InstallAsync("1.0.0");
        _catalog.Archive = MakeArchive("2.0.0");
        await InstallAsync("2.0.0");
        _catalog.Archive = MakeArchive();

        await InstallAsync("1.0.0");

        using var templater = _factory.Create(Context());
        var installed = (await templater.GetManagedPackagesAsync(TestContext.Current.CancellationToken)).Single();
        installed.Version.Should().Be("1.0.0");
        File.ReadAllBytes(installed.MountPointUri).Should().Equal(_catalog.Archive);
        _factory.Installations.Should().Be(3);
        AssertTemporaryFilesRemoved();
    }

    [Fact]
    public async Task Install_ExplicitPrereleaseVersionDoesNotRequirePrereleaseSwitch()
    {
        _catalog.Archive = MakeArchive("1.0.0-preview.1");

        await InstallAsync("1.0.0-preview.1");

        await AssertInstalledVersionAsync("1.0.0-preview.1");
    }

    [Theory]
    [InlineData("2.0.0")]
    [InlineData("2.0.0-preview.1")]
    public async Task Install_ArchiveVersionMismatchDoesNotReplace(string requestedVersion)
    {
        await InstallAsync("1.0.0");

        Func<Task> act = () => InstallAsync(requestedVersion);

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*does not match*");
        _factory.Installations.Should().Be(1);
        _factory.Uninstallations.Should().Be(0);
        AssertTemporaryFilesRemoved();
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Theory]
    [InlineData("Dependency", false)]
    [InlineData("Template", false)]
    [InlineData("FuncTemplate,FuncCliWorkload", false)]
    [InlineData("FuncTemplate,FuncCliWorkloadRidPackage", false)]
    [InlineData("FuncTemplate", true)]
    public async Task Install_InvalidPackageTypeOrHostMetadataFailsBeforeMutation(string packageTypes, bool brokenHost)
    {
        await InstallAsync("1.0.0");
        _catalog.Archive = MakeArchive("2.0.0", packageTypes, brokenHost ? "{" : null);

        Func<Task> act = () => InstallAsync("2.0.0");

        await act.Should().ThrowAsync<TemplatePackageException>();
        _factory.Installations.Should().Be(1);
        _factory.Uninstallations.Should().Be(0);
        AssertTemporaryFilesRemoved();
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Install_RejectsEmptyOrUndiscoverableDeclarationsAndPreservesPreviousInstallation(bool includeTemplates, bool invalidTemplate)
    {
        await InstallAsync("1.0.0");
        int installations = _factory.Installations;
        _catalog.Archive = MakeArchive("2.0.0", includeTemplates: includeTemplates, invalidTemplate: invalidTemplate);

        Func<Task> act = () => InstallAsync("2.0.0");

        await act.Should().ThrowAsync<TemplatePackageException>();
        _factory.Installations.Should().Be(includeTemplates ? installations + 2 : installations);
        _factory.Uninstallations.Should().Be(includeTemplates ? 2 : 0);
        AssertTemporaryFilesRemoved();
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Fact]
    public async Task Install_PackageIdMismatchFailsBeforeMutation()
    {
        await InstallAsync("1.0.0");
        _catalog.Archive = MakeArchive("2.0.0", packageId: "other.templates");

        Func<Task> act = () => InstallAsync("2.0.0");

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*does not match*");
        _factory.Installations.Should().Be(1);
        _factory.Uninstallations.Should().Be(0);
        AssertTemporaryFilesRemoved();
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Fact]
    public async Task Install_CacheFailureRestoresPreviousRegistryArchiveAndDiscovery()
    {
        await InstallAsync("1.0.0");
        _catalog.Archive = MakeArchive("2.0.0");
        _factory.FailCache = true;

        Func<Task> act = () => InstallAsync("2.0.0");

        await act.Should().ThrowAsync<IOException>().WithMessage("cache failed");
        File.Exists(_factory.Request!.PackageIdentifier).Should().BeFalse();
        AssertTemporaryFilesRemoved();
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Fact]
    public async Task Install_CacheFailureWithoutPreviousPackageRemovesPartialInstallation()
    {
        _factory.FailCache = true;

        Func<Task> act = () => InstallAsync("1.0.0");

        await act.Should().ThrowAsync<IOException>().WithMessage("cache failed");
        AssertTemporaryFilesRemoved();
        using var templater = _factory.Create(Context());
        (await templater.GetManagedPackagesAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
        (await templater.ListAsync(TestContext.Current.CancellationToken)).Templates.Should().BeEmpty();
    }

    [Fact]
    public async Task Install_ProviderFailureAfterMutationRestoresPreviousInstallation()
    {
        await InstallAsync("1.0.0");
        _catalog.Archive = MakeArchive("2.0.0");
        _factory.FailInstall = true;

        Func<Task> act = () => InstallAsync("2.0.0");

        await act.Should().ThrowAsync<IOException>().WithMessage("provider failed");
        File.Exists(_factory.Request!.PackageIdentifier).Should().BeFalse();
        AssertTemporaryFilesRemoved();
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Fact]
    public async Task Uninstall_CacheFailureRestoresPreviousInstallation()
    {
        await InstallAsync("1.0.0");
        _factory.FailCache = true;

        Func<Task> act = () => _service.UninstallAsync("test.templates", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<IOException>().WithMessage("cache failed");
        AssertTemporaryFilesRemoved();
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Fact]
    public async Task Install_RecoveryFailureReportsIncompleteEffectsAndCleansTemporaryFiles()
    {
        await InstallAsync("1.0.0");
        _catalog.Archive = MakeArchive("2.0.0");
        _factory.FailCache = true;
        _factory.FailRecoveryInstall = true;

        Func<Task> act = () => InstallAsync("2.0.0");

        var failure = (await act.Should().ThrowAsync<TemplatePackageException>()
            .WithMessage("*missing or partially installed*Recovery failed*cannot recover*")).Which;
        failure.InnerException.Should().BeOfType<IOException>().Which.Message.Should().Be("cannot recover");
        AssertTemporaryFilesRemoved();
        using var templater = _factory.Create(Context());
        (await templater.GetManagedPackagesAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task Install_CancellationDuringMutationRestoresPreviousInstallation()
    {
        await InstallAsync("1.0.0");
        _catalog.Archive = MakeArchive("2.0.0");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _factory.CancelCache = cancellation;

        Func<Task> act = () => _service.InstallAsync("test.templates", "2.0.0", null, false, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(_factory.Request!.PackageIdentifier).Should().BeFalse();
        _factory.CancelCache = null;
        AssertTemporaryFilesRemoved();
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Fact]
    public async Task Install_CancellationBeforeMutationLeavesPreviousInstallationUntouched()
    {
        await InstallAsync("1.0.0");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        Func<Task> act = () => _service.InstallAsync("test.templates", "2.0.0", null, false, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _factory.Installations.Should().Be(1);
        _factory.Uninstallations.Should().Be(0);
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Fact]
    public async Task Install_BackupFailureNeverMutatesPreviousInstallation()
    {
        await InstallAsync("1.0.0");
        _catalog.Archive = MakeArchive("2.0.0");
        _fileSystem.When(f => f.CopyFile(Arg.Any<string>(), Arg.Any<string>()))
            .Do(_ => throw new IOException("backup failed"));

        Func<Task> act = () => InstallAsync("2.0.0");

        await act.Should().ThrowAsync<IOException>().WithMessage("backup failed");
        _factory.Installations.Should().Be(1);
        _factory.Uninstallations.Should().Be(0);
        AssertTemporaryFilesRemoved();
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Fact]
    public async Task Uninstall_WorkloadOwnedIdReportsGuidanceAndNeverRemovesRoot()
    {
        _workloads.GetRootsAsync(Arg.Any<CancellationToken>()).Returns(
            [new WorkloadTemplateRoot(_root, "workload.templates", "1.0.0", DateTime.UtcNow)]);

        Func<Task> act = () => _service.UninstallAsync("workload.templates", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*func workload uninstall workload.templates*");
        Directory.Exists(_root).Should().BeTrue();
    }

    private Task<bool> InstallAsync(string version)
        => _service.InstallAsync("test.templates", version, null, false, TestContext.Current.CancellationToken);

    private async Task AssertInstalledVersionAsync(string version)
    {
        using var templater = _factory.Create(Context());
        (await templater.GetManagedPackagesAsync(TestContext.Current.CancellationToken)).Should().ContainSingle().Which.Version.Should().Be(version);
        (await templater.ListAsync(TestContext.Current.CancellationToken)).Templates.Should().HaveCount(4);
    }

    private TemplateEngineContext Context() => new(WorkingDirectory.FromExplicit(_root));

    private void AssertTemporaryFilesRemoved() => _temporaryDirectories.Should().OnlyContain(path => !Directory.Exists(path));

    private async Task<byte[]> SignArchiveAsync(byte[] archive)
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=Template Tests Root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(3));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Template Tests Author", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.3")], true));
        using var certificate = request.Create(root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1),
            RandomNumberGenerator.GetBytes(16));
        using var author = certificate.CopyWithPrivateKey(key);
        using var signingCertificate = new X509Certificate2(author);
        using var signRequest = new AuthorSignPackageRequest(signingCertificate, NuGet.Common.HashAlgorithmName.SHA256);
        using var input = new MemoryStream(archive);
        using var output = new MemoryStream();
        using (var signed = new SignedPackageArchive(input, output))
        {
            byte[] hash = await signed.GetArchiveHashAsync(NuGet.Common.HashAlgorithmName.SHA256, TestContext.Current.CancellationToken);
            var content = new SignatureContent(SigningSpecifications.V1, NuGet.Common.HashAlgorithmName.SHA256, Convert.ToBase64String(hash));
            var cms = new SignedCms(new ContentInfo(content.GetBytes()));
            var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, author)
            {
                IncludeOption = X509IncludeOption.EndCertOnly,
                DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"),
            };
            signer.Certificates.Add(root);
            foreach (CryptographicAttributeObject attribute in SigningUtility.CreateSignedAttributes(signRequest, [author, root]))
            {
                signer.SignedAttributes.Add(attribute);
            }

            cms.ComputeSignature(signer);
            using var signature = new MemoryStream(cms.Encode());
            await signed.AddSignatureAsync(signature, TestContext.Current.CancellationToken);
        }

        return output.ToArray();
    }

    private byte[] MakeArchive(
        string version = "1.0.0", string types = "FuncTemplate", string? hostJson = null, bool includeTemplates = true,
        bool invalidTemplate = false, string packageId = "test.templates")
    {
        string directory = TemplateFixtures.WriteBasicPackage(_root, hostJson);
        if (invalidTemplate)
        {
            File.WriteAllText(Path.Combine(directory, "item", ".template.config", "template.json"), """{"identity":"Invalid"}""");
        }

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string path in includeTemplates ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories) : [])
            {
                archive.CreateEntryFromFile(path, Path.GetRelativePath(directory, path).Replace('\\', '/'));
            }

            using var writer = new StreamWriter(archive.CreateEntry("test.templates.nuspec").Open());
            writer.Write($"""
                <package><metadata><id>{packageId}</id><version>{version}</version><authors>test</authors>
                <description>Test templates</description><packageTypes>
                {string.Join("", types.Split(',').Select(t => $"<packageType name=\"{t}\" />"))}
                </packageTypes></metadata></package>
                """);
        }

        return output.ToArray();
    }

    private sealed class LoopbackTemplateFeed : IAsyncDisposable
    {
        private const string PackageId = "test.templates";
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Dictionary<string, byte[]> _packages = [];
        private readonly Lock _gate = new();
        private readonly string _address;
        private readonly Task _server;

        public LoopbackTemplateFeed()
        {
            _listener.Start();
            _address = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _server = ServeAsync(_stop.Token);
        }

        public string Source => _address + "index.json";

        public void Add(string version, byte[] package)
        {
            lock (_gate)
            {
                _packages[version.ToLowerInvariant()] = package;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try
            {
                await _server;
            }
            catch (Exception ex) when (_stop.IsCancellationRequested && ex is OperationCanceledException or SocketException)
            {
                // Stopping the listener aborts the pending accept; that is the expected shutdown path.
            }
            finally
            {
                _stop.Dispose();
            }
        }

        private byte[]? Resolve(string path)
        {
            lock (_gate)
            {
                if (path == "/index.json")
                {
                    return Encoding.UTF8.GetBytes(
                        $$"""{"version":"3.0.0","resources":[{"@id":"{{_address}}flat/","@type":"PackageBaseAddress/3.0.0"},{"@id":"{{_address}}registration/","@type":"RegistrationsBaseUrl/3.6.0"}]}""");
                }

                if (path == $"/registration/{PackageId}/index.json")
                {
                    string items = string.Join(",", _packages.Keys.Select(version =>
                        $$"""{"catalogEntry":{"id":"{{PackageId}}","version":"{{version}}","listed":true},"packageContent":"{{_address}}flat/{{PackageId}}/{{version}}/{{PackageId}}.{{version}}.nupkg"}"""));
                    return Encoding.UTF8.GetBytes(
                        $$"""{"count":1,"items":[{"count":{{_packages.Count}},"lower":"0.0.0","upper":"999.0.0","items":[{{items}}]}]}""");
                }

                if (path == $"/flat/{PackageId}/index.json")
                {
                    string versions = string.Join(",", _packages.Keys.Select(v => $"\"{v}\""));
                    return Encoding.UTF8.GetBytes($$"""{"versions":[{{versions}}]}""");
                }

                foreach ((string version, byte[] package) in _packages)
                {
                    if (path == $"/flat/{PackageId}/{version}/{PackageId}.{version}.nupkg")
                    {
                        return package;
                    }
                }

                return null;
            }
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
                await using NetworkStream stream = client.GetStream();
                using StreamReader reader = new(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                string request = await reader.ReadLineAsync(cancellationToken) ?? throw new InvalidDataException("Missing HTTP request.");
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
                {
                }

                byte[]? body = Resolve(request.Split(' ')[1]);
                string headers = $"HTTP/1.1 {(body is null ? "404 Not Found" : "200 OK")}\r\nContent-Length: {body?.Length ?? 0}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken);
                await stream.WriteAsync(body ?? [], cancellationToken);
            }
        }
    }

    private sealed class ArchiveCatalog(string archivePath) : TemplateCatalog(Options.Create(new TemplateCatalogOptions
    {
        Source = "https://feed.test/index.json",
    }), _ => throw new InvalidOperationException("No networking in engine tests."))
    {
        public override Task<Stream> OpenPackageAsync(
            string packageId, NuGetVersion version, string? source, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Stream>(File.OpenRead(archivePath));
        }

        internal byte[] Archive
        {
            get => File.ReadAllBytes(archivePath);
            set => File.WriteAllBytes(archivePath, value);
        }
    }

    private sealed class TestFactory(string hive) : ITemplaterFactory
    {
        public string HivePath => hive;

        internal bool FailCache { get; set; }

        internal bool FailInstall { get; set; }

        internal bool FailRecoveryInstall { get; set; }

        internal int Uninstallations { get; set; }

        internal int Installations { get; set; }

        internal InstallRequest? Request { get; set; }

        internal CancellationTokenSource? CancelCache { get; set; }

        public Templater Create(TemplateEngineContext context) => new FaultTemplater(context, HivePath, this);

        private sealed class FaultTemplater(TemplateEngineContext context, string hive, TestFactory factory)
            : Templater(context, hive, new FuncTemplateComponentsProvider([], [], Substitute.For<IInteractionService>()).Get())
        {
            internal override async Task<InstallResult> InstallAsync(InstallRequest request, CancellationToken cancellationToken)
            {
                factory.Installations++;
                factory.Request = request;
                if (factory.FailRecoveryInstall && Path.GetFileName(request.PackageIdentifier) == "previous.nupkg")
                {
                    throw new IOException("cannot recover");
                }

                InstallResult result = await base.InstallAsync(request, cancellationToken);
                if (factory.FailInstall)
                {
                    factory.FailInstall = false;
                    throw new IOException("provider failed");
                }

                return result;
            }

            internal override async Task<UninstallResult> UninstallAsync(IManagedTemplatePackage package, CancellationToken cancellationToken)
            {
                factory.Uninstallations++;
                return await base.UninstallAsync(package, cancellationToken);
            }

            internal override async Task RefreshAsync(CancellationToken cancellationToken)
            {
                await base.RefreshAsync(cancellationToken);
                if (factory.CancelCache is { } cancellation)
                {
                    factory.CancelCache = null;
                    await cancellation.CancelAsync();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (factory.FailCache)
                {
                    factory.FailCache = false;
                    throw new IOException("cache failed");
                }
            }
        }
    }
}
