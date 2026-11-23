// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Packages;
using Microsoft.Extensions.Options;
using Microsoft.TemplateEngine.Abstractions;
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

    public TemplatePackageServiceTests()
    {
        Directory.CreateDirectory(_root);
        _catalog = new ArchiveCatalog(Path.Combine(_root, "fixture.nupkg"));
        _factory = new TestFactory(Path.Combine(_root, "templates"));
        _workloads.GetRootsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var fileSystem = new PhysicalFileSystem();
        _verifier = new TemplatePackageVerifier(new TemplateNuGetSourceProvider(new DirectoryInfo(_root)));
        _service = new TemplatePackageService(_catalog, new TemplatePackageValidator(), _factory,
            new TemplateHiveTransaction(fileSystem, _factory), fileSystem, _workloads, _verifier);
        _catalog.Archive = MakeArchive();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Verifier_NullSourceProviderIsRejected()
    {
        Action act = () => _ = new TemplatePackageVerifier(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("sources");
    }

    [Fact]
    public async Task Verifier_NullArchiveIsRejected()
    {
        Func<Task> act = () => _verifier.VerifyAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("archive");
    }

    [Fact]
    public async Task Install_UnsignedPackageIsRejectedWhenConfigurationRequiresSigning()
    {
        File.WriteAllText(Path.Combine(_root, "NuGet.Config"), """
            <configuration><config><add key="signatureValidationMode" value="require" /></config></configuration>
            """);

        Func<Task> act = () => InstallAsync("1.0.0");

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*unsigned*requires signing*");
        _factory.Installations.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Install_SignatureWithUntrustedRootFollowsConfiguredValidationMode(bool require)
    {
        if (require)
        {
            File.WriteAllText(Path.Combine(_root, "NuGet.Config"), """
                <configuration><config><add key="signatureValidationMode" value="require" /></config></configuration>
                """);
        }

        (_catalog.Archive, _) = await SignArchiveAsync(_catalog.Archive);

        Func<Task> act = () => InstallAsync("1.0.0");

        if (require)
        {
            await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*signature*");
            _factory.Installations.Should().Be(0);
        }
        else
        {
            await act.Should().NotThrowAsync();
            await AssertInstalledVersionAsync("1.0.0");
        }
    }

    [Fact]
    public async Task Install_SignedPackageWorksWithoutDotnetOnPathAndOwnsItsArchive()
    {
        var (archive, fingerprint) = await SignArchiveAsync(_catalog.Archive);
        string feed = Path.Combine(_root, "signed-feed");
        string home = Path.Combine(_root, "signed-home");
        Directory.CreateDirectory(feed);
        File.WriteAllBytes(Path.Combine(feed, "test.templates.1.0.0.nupkg"), archive);
        WriteTrustedSignerConfig(fingerprint, feed);

        await RunCliInstallAsync(home, 0, "--version", "1.0.0");

        var factory = new TemplaterFactory(new CliConfigurationPathsOptions(home), []);
        await AssertCliPackageAsync(factory, "1.0.0", feed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Install_InvalidSignatureLeavesHiveUntouchedAndCleansDownload(bool tamper)
    {
        await InstallAsync("1.0.0");
        int installations = _factory.Installations;
        var (archive, fingerprint) = await SignArchiveAsync(MakeArchive("2.0.0"));
        if (tamper)
        {
            WriteTrustedSignerConfig(fingerprint);
            using var stream = new MemoryStream();
            stream.Write(archive);
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
            {
                using var writer = new StreamWriter(zip.CreateEntry("tampered.txt").Open());
                writer.Write("changed after signing");
            }

            archive = stream.ToArray();
        }
        else
        {
            WriteTrustedSignerConfig(new string('0', 64));
        }

        _catalog.Archive = archive;
        var physical = new PhysicalFileSystem();
        var fileSystem = Substitute.For<IFileSystem>();
        string downloadPath = Path.Combine(_root, "failed-download");
        Directory.CreateDirectory(downloadPath);
        fileSystem.CreateTempDirectory().Returns(new TempDirectory(downloadPath, physical));
        fileSystem.SaveStreamToFileAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(call => physical.SaveStreamToFileAsync(call.ArgAt<string>(0), call.ArgAt<Stream>(1), call.ArgAt<CancellationToken>(2)));
        fileSystem.OpenReadIfExists(Arg.Any<string>()).Returns(call => physical.OpenReadIfExists(call.ArgAt<string>(0)));
        var service = new TemplatePackageService(_catalog, new TemplatePackageValidator(), _factory,
            new TemplateHiveTransaction(physical, _factory), fileSystem, _workloads, _verifier);

        Func<Task> act = () => service.InstallAsync("test.templates", "2.0.0", null, false, CancellationToken.None);

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*signature*");
        Directory.Exists(downloadPath).Should().BeFalse();
        _factory.Installations.Should().Be(installations);
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Fact]
    public async Task Install_UsesAmbientFeedFiltersHttpAndRollsBackInvalidReplacement()
    {
        string feed = Path.Combine(_root, "feed");
        string home = Path.Combine(_root, "cli-home");
        Directory.CreateDirectory(feed);
        File.WriteAllBytes(Path.Combine(feed, "test.templates.1.0.0.nupkg"), _catalog.Archive);
        File.WriteAllBytes(Path.Combine(feed, "test.templates.3.0.0-preview.1.nupkg"), MakeArchive("3.0.0-preview.1"));
        File.WriteAllText(Path.Combine(_root, "NuGet.Config"), $"""
            <configuration>
              <packageSources><clear /><add key="fixture" value="{System.Security.SecurityElement.Escape(feed)}" /></packageSources>
              <packageSourceMapping><clear /></packageSourceMapping>
            </configuration>
            """);
        var factory = new TemplaterFactory(new CliConfigurationPathsOptions(home), []);

        await RunCliInstallAsync(home, 0);
        await AssertCliPackageAsync(factory, "1.0.0", feed);
        await RunCliInstallAsync(home, 0, "--prerelease");
        await AssertCliPackageAsync(factory, "3.0.0-preview.1", feed);
        await RunCliInstallAsync(home, 0, "--version", "1.0.0");
        File.WriteAllBytes(Path.Combine(feed, "test.templates.2.0.0.nupkg"), MakeArchive("2.0.0", "FuncTemplate,FuncCliWorkloadRidPackage"));
        await RunCliInstallAsync(home, 1, "--version", "2.0.0");
        await AssertCliPackageAsync(factory, "1.0.0", feed);
    }

    private async Task RunCliInstallAsync(string home, int expectedExitCode, params string[] options)
    {
        string runtime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        string dotnet = Path.GetFullPath(Path.Combine(runtime, "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var testOutput = new DirectoryInfo(Path.GetDirectoryName(typeof(Templater).Assembly.Location)!);
        string cli = Path.GetFullPath(Path.Combine(testOutput.FullName, "..", "..", "Func", testOutput.Name, "func.dll"));
        var start = new ProcessStartInfo(dotnet)
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[] { cli, "templates", "install", "test.templates",
            "--source", "http://127.0.0.1:1/v3/index.json" }.Concat(options))
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["FUNC_CLI_HOME"] = home;
        start.Environment.Remove("FUNC_CLI_TEMPLATES_SOURCE");
        start.Environment["PATH"] = string.Empty;
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            process.ExitCode.Should().Be(expectedExitCode, await output + await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private async Task AssertCliPackageAsync(TemplaterFactory factory, string version, string feed)
    {
        using Templater templater = factory.Create(Context());
        var package = (await templater.GetManagedPackagesAsync(CancellationToken.None)).Should().ContainSingle().Subject;
        package.Version.Should().Be(version);
        package.IsLocalPackage.Should().BeTrue();
        package.GetDetails().Should().NotContainKey("NuGetSource");
        File.Exists(package.MountPointUri).Should().BeTrue();
        package.MountPointUri.Should().StartWith(factory.HivePath);
        File.ReadAllBytes(package.MountPointUri).Should().Equal(File.ReadAllBytes(Path.Combine(feed, $"test.templates.{version}.nupkg")));
        (await templater.ListAsync(CancellationToken.None)).Templates.Should().HaveCount(4);
    }

    [Fact]
    public async Task InstallAndUninstall_AreVisibleToFreshDiscoveryAndUseManagedNuGetIdentity()
    {
        await InstallAsync("1.0.0");

        using (var templater = _factory.Create(Context()))
        {
            (await templater.ListAsync(CancellationToken.None)).Templates.Should().HaveCount(4);
            var installed = (await templater.GetManagedPackagesAsync(CancellationToken.None)).Should().ContainSingle().Subject;
            installed.Identifier.Should().Be("test.templates");
            _factory.Request!.PackageIdentifier.Should().EndWith("package.nupkg");
            File.Exists(_factory.Request.PackageIdentifier).Should().BeFalse("the download is cleaned up after installation");
            installed.MountPointUri.Should().StartWith(_factory.HivePath).And.NotBe(_factory.Request.PackageIdentifier);
            File.ReadAllBytes(installed.MountPointUri).Should().Equal(_catalog.Archive);
            _factory.Request.Details.Should().NotContainKey("NuGetSources");
            _factory.Request.Force.Should().BeFalse();
        }

        await _service.UninstallAsync("TEST.TEMPLATES", CancellationToken.None);

        using var after = _factory.Create(Context());
        (await after.ListAsync(CancellationToken.None)).Templates.Should().BeEmpty();
    }

    [Fact]
    public async Task Install_SameVersionIsNoOpRegardlessOfAdditionalSource()
    {
        await InstallAsync("1.0.0");
        int installations = _factory.Installations;

        bool changed = await InstallAsync("1.0.0");
        bool sourceChange = await _service.InstallAsync("test.templates", "1.0.0", "https://other.test/index.json", false, CancellationToken.None);

        changed.Should().BeFalse();
        sourceChange.Should().BeFalse();
        _factory.Installations.Should().Be(installations);
    }

    [Fact]
    public async Task Install_PassesOnlyTheStagedArchiveToTheEngine()
    {
        await _service.InstallAsync("test.templates", "1.0.0", null, false, CancellationToken.None);

        _factory.Request!.Details.Should().NotContainKey("NuGetSources");
        _factory.Request.Version.Should().BeNull();
        _factory.Request.Force.Should().BeFalse();
    }

    [Fact]
    public async Task Install_ReplacesAndAllowsExplicitDowngradeAndValidatesInstalledBytes()
    {
        await InstallAsync("1.0.0");
        _catalog.Archive = MakeArchive("2.0.0");
        await InstallAsync("2.0.0");
        _catalog.Archive = MakeArchive();

        await InstallAsync("1.0.0");

        using var templater = _factory.Create(Context());
        var installed = (await templater.GetManagedPackagesAsync(CancellationToken.None)).Single();
        installed.Version.Should().Be("1.0.0");
        File.ReadAllBytes(installed.MountPointUri).Should().Equal(_catalog.Archive);
        _factory.Installations.Should().Be(3);
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
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Theory]
    [InlineData("Dependency", false)]
    [InlineData("FuncTemplate,FuncCliWorkload", false)]
    [InlineData("FuncTemplate,FuncCliWorkloadRidPackage", false)]
    [InlineData("FuncTemplate", true)]
    public async Task Install_RejectsInvalidInstalledArchivesAndRestoresPreviousInstallation(string packageTypes, bool brokenHost)
    {
        await InstallAsync("1.0.0");
        _catalog.Archive = MakeArchive("2.0.0", packageTypes, brokenHost ? "{" : null);

        Func<Task> act = () => InstallAsync("2.0.0");

        await act.Should().ThrowAsync<TemplatePackageException>();
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
        _factory.Installations.Should().Be(installations + 1);
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("C:/escape")]
    [InlineData("/escape")]
    [InlineData("folder\\..\\escape")]
    public async Task Install_UnsafeArchivePathsDoNotReplace(string unsafePath)
    {
        await InstallAsync("1.0.0");
        _catalog.Archive = MakeArchive("2.0.0", extraEntry: unsafePath);

        Func<Task> act = () => InstallAsync("2.0.0");

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*safe relative package path*");
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
        _factory.FailCache = false;
        await AssertInstalledVersionAsync("1.0.0");
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
        _factory.FailInstall = false;
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Fact]
    public async Task Uninstall_CacheFailureRestoresPreviousInstallation()
    {
        await InstallAsync("1.0.0");
        _factory.FailCache = true;

        Func<Task> act = () => _service.UninstallAsync("test.templates", CancellationToken.None);

        await act.Should().ThrowAsync<IOException>().WithMessage("cache failed");
        _factory.FailCache = false;
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Fact]
    public async Task Install_RecoveryFailureReportsAndRetainsRecoveryCopy()
    {
        await InstallAsync("1.0.0");
        _catalog.Archive = MakeArchive("2.0.0");
        _factory.FailCache = true;
        var physical = new PhysicalFileSystem();
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.DirectoryExists(Arg.Any<string>()).Returns(c => physical.DirectoryExists(c.Arg<string>()));
        fileSystem.CreateTempDirectory(Arg.Any<string>()).Returns(c => physical.CreateTempDirectory(c.Arg<string>()));
        fileSystem.OpenReadIfExists(Arg.Any<string>()).Returns(c => physical.OpenReadIfExists(c.Arg<string>()));
        fileSystem.When(f => f.CreateDirectory(Arg.Any<string>())).Do(c => physical.CreateDirectory(c.Arg<string>()));
        fileSystem.When(f => f.DeleteDirectory(Arg.Any<string>())).Do(c => physical.DeleteDirectory(c.Arg<string>()));
        fileSystem.When(f => f.CopyDirectory(Arg.Any<string>(), Arg.Any<string>())).Do(c =>
        {
            if (c.ArgAt<string>(0).EndsWith(Path.DirectorySeparatorChar + "hive", StringComparison.Ordinal))
            {
                throw new IOException("cannot recover");
            }

            physical.CopyDirectory(c.ArgAt<string>(0), c.ArgAt<string>(1));
        });
        var service = new TemplatePackageService(_catalog, new TemplatePackageValidator(), _factory,
            new TemplateHiveTransaction(fileSystem, _factory), fileSystem, _workloads, _verifier);
        fileSystem.CreateTempDirectory().Returns(_ => physical.CreateTempDirectory());
        fileSystem.SaveStreamToFileAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(c => physical.SaveStreamToFileAsync(c.ArgAt<string>(0), c.ArgAt<Stream>(1), c.ArgAt<CancellationToken>(2)));

        Func<Task> act = () => service.InstallAsync("test.templates", "2.0.0", null, false, CancellationToken.None);

        var failure = (await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*Hive recovery failed*preserved*")).Which;
        failure.InnerException.Should().BeOfType<IOException>();
        Directory.GetFiles(_root, "packages.json", SearchOption.AllDirectories).Should().ContainSingle();
    }

    [Fact]
    public async Task Install_CancellationDuringMutationRestoresPreviousInstallation()
    {
        await InstallAsync("1.0.0");
        _catalog.Archive = MakeArchive("2.0.0");
        using var cancellation = new CancellationTokenSource();
        _factory.CancelCache = cancellation;

        Func<Task> act = () => _service.InstallAsync("test.templates", "2.0.0", null, false, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(_factory.Request!.PackageIdentifier).Should().BeFalse();
        _factory.CancelCache = null;
        await AssertInstalledVersionAsync("1.0.0");
    }

    [Fact]
    public async Task Transaction_CancellationBeforeSnapshotDoesNotTouchHive()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        var transaction = new TemplateHiveTransaction(fileSystem, _factory);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Func<Task> act = () => transaction.ExecuteAsync(Context(), (_, _) => Task.CompletedTask, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fileSystem.DidNotReceive().CreateTempDirectory(Arg.Any<string>());
        fileSystem.DidNotReceive().CopyDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Transaction_IncompleteSnapshotNeverMutatesOrRestoresLiveHive()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        string recovery = Path.Combine(_root, "recovery");
        fileSystem.DirectoryExists(Arg.Any<string>()).Returns(true);
        fileSystem.CreateTempDirectory(_root).Returns(new TempDirectory(recovery, fileSystem));
        fileSystem.When(f => f.CopyDirectory(_factory.HivePath, Path.Combine(recovery, "hive")))
            .Do(_ => throw new IOException("snapshot failed"));
        var transaction = new TemplateHiveTransaction(fileSystem, _factory);
        bool mutated = false;

        Func<Task> act = () => transaction.ExecuteAsync(Context(), (_, _) =>
        {
            mutated = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        await act.Should().ThrowAsync<IOException>().WithMessage("snapshot failed");
        mutated.Should().BeFalse();
        fileSystem.DidNotReceive().DeleteDirectory(_factory.HivePath);
        fileSystem.Received(1).DeleteDirectory(recovery);
    }

    [Fact]
    public async Task Uninstall_WorkloadOwnedIdReportsGuidanceAndNeverRemovesRoot()
    {
        _workloads.GetRootsAsync(Arg.Any<CancellationToken>()).Returns(
            [new WorkloadTemplateRoot(_root, "workload.templates", "1.0.0", DateTime.UtcNow)]);

        Func<Task> act = () => _service.UninstallAsync("workload.templates", CancellationToken.None);

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*func workload uninstall workload.templates*");
        Directory.Exists(_root).Should().BeTrue();
    }

    private Task<bool> InstallAsync(string version)
        => _service.InstallAsync("test.templates", version, null, false, CancellationToken.None);

    private async Task AssertInstalledVersionAsync(string version)
    {
        using var templater = _factory.Create(Context());
        (await templater.GetManagedPackagesAsync(CancellationToken.None)).Should().ContainSingle().Which.Version.Should().Be(version);
        (await templater.ListAsync(CancellationToken.None)).Templates.Should().HaveCount(4);
    }

    private TemplateEngineContext Context() => new(WorkingDirectory.FromExplicit(_root));

    private void WriteTrustedSignerConfig(string fingerprint, string? feed = null)
    {
        File.WriteAllText(Path.Combine(_root, "NuGet.Config"), $"""
            <configuration>
              <packageSources><clear />{(feed is null ? "" : $"<add key=\"fixture\" value=\"{System.Security.SecurityElement.Escape(feed)}\" />")}</packageSources>
              <packageSourceMapping><clear /></packageSourceMapping>
              <trustedSigners><clear /><author name="fixture">
                <certificate fingerprint="{fingerprint}" hashAlgorithm="SHA256" allowUntrustedRoot="true" />
              </author></trustedSigners>
            </configuration>
            """);
    }

    private async Task<(byte[] Archive, string Fingerprint)> SignArchiveAsync(byte[] archive)
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
            byte[] hash = await signed.GetArchiveHashAsync(NuGet.Common.HashAlgorithmName.SHA256, CancellationToken.None);
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
            await signed.AddSignatureAsync(signature, CancellationToken.None);
        }

        return (output.ToArray(), author.GetCertHashString(HashAlgorithmName.SHA256));
    }

    private byte[] MakeArchive(
        string version = "1.0.0", string types = "FuncTemplate", string? hostJson = null, bool includeTemplates = true,
        bool invalidTemplate = false, string? extraEntry = null)
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

            if (extraEntry is not null)
            {
                using var entry = new StreamWriter(archive.CreateEntry(extraEntry).Open());
                entry.Write("unsafe entry");
            }

            using var writer = new StreamWriter(archive.CreateEntry("test.templates.nuspec").Open());
            writer.Write($"""
                <package><metadata><id>test.templates</id><version>{version}</version><authors>test</authors>
                <description>Test templates</description><packageTypes>
                {string.Join("", types.Split(',').Select(t => $"<packageType name=\"{t}\" />"))}
                </packageTypes></metadata></package>
                """);
        }

        return output.ToArray();
    }

    private sealed class ArchiveCatalog(string archivePath) : TemplateCatalog(Options.Create(new TemplateCatalogOptions
    {
        Source = "https://feed.test/index.json",
    }), _ => throw new InvalidOperationException("No networking in engine tests."),
        Substitute.For<TemplateNuGetSourceProvider>(new DirectoryInfo(Directory.GetCurrentDirectory())))
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

        internal int Installations { get; set; }

        internal InstallRequest? Request { get; set; }

        internal CancellationTokenSource? CancelCache { get; set; }

        public Templater Create(TemplateEngineContext context) => new FaultTemplater(context, HivePath, this);

        private sealed class FaultTemplater(TemplateEngineContext context, string hive, TestFactory factory)
            : Templater(context, hive, [])
        {
            internal override async Task<InstallResult> InstallAsync(InstallRequest request, CancellationToken cancellationToken)
            {
                factory.Installations++;
                factory.Request = request;
                InstallResult result = await base.InstallAsync(request, cancellationToken);
                if (factory.FailInstall)
                {
                    throw new IOException("provider failed");
                }

                return result;
            }

            internal override async Task<IEnumerable<ITemplateInfo>> GetTemplatesAsync(ITemplatePackage package, CancellationToken cancellationToken)
            {
                IEnumerable<ITemplateInfo> templates = await base.GetTemplatesAsync(package, cancellationToken);
                if (factory.CancelCache is { } cancellation)
                {
                    await cancellation.CancelAsync();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (factory.FailCache)
                {
                    throw new IOException("cache failed");
                }

                return templates;
            }
        }
    }

}
