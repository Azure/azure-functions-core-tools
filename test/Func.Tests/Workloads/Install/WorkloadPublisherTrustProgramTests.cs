// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Net.Sockets;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed class WorkloadPublisherTrustProgramTests(ITestOutputHelper output) : WorkloadInstallerTestBase
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task UnsignedDevelopmentJourney_StrictRejectsBypassInstallsAndUpdatePreservesPriorVersion()
    {
        const string PackageId = "azure.functions.cli.workloads.test.content";
        string manifest = WriteTempFile("workload.json", $$"""{"$schema":"{{WorkloadManifestSchema.PackageManifestV1Schema}}","kind":"content"}""");
        string oldPackage = BuildNupkg(id: PackageId, tags: "kind:content", version: "1.0.0",
            extraFiles: [(manifest, "workload.json")]);
        string newPackage = BuildNupkg(id: PackageId, tags: "kind:content", version: "1.1.0",
            extraFiles: [(manifest, "workload.json")]);
        string feed = Directory.CreateDirectory(Path.Combine(Root, "feed")).FullName;
        string oldSource = Path.Combine(feed, $"{PackageId}.1.0.0.nupkg");
        string newSource = Path.Combine(feed, $"{PackageId}.1.1.0.nupkg");
        File.Copy(oldPackage, oldSource);
        File.Copy(newPackage, newSource);
        byte[] oldBytes = await File.ReadAllBytesAsync(oldSource);
        byte[] newBytes = await File.ReadAllBytesAsync(newSource);
        await using LoopbackNuGetFeed catalog = new(PackageId, newBytes);
        CliConfigurationPathsOptions paths = new(Path.Combine(Root, "home"));
        WorkloadPathsOptions workloads = new(Path.Combine(Root, "workload-home"));
        Directory.CreateDirectory(paths.Home);
        await File.WriteAllTextAsync(paths.VersionCachePath, AssemblyCliVersionProvider.Instance.Version.Split('-')[0]);
        CliProcessTestRunner runner = new(output);
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(60));

        var rejectedInstall = await RunAsync(["workload", "install", oldSource]);
        rejectedInstall.ExitCode.Should().Be(1);
        (rejectedInstall.StandardOutput + rejectedInstall.StandardError).Should().Contain("publisher verification failed");
        File.Exists(workloads.WorkloadRegistryPath).Should().BeFalse();

        var installed = await RunAsync(["workload", "install", oldSource, "--allow-untrusted"]);
        installed.ExitCode.Should().Be(0, installed.StandardOutput + installed.StandardError);
        installed.StandardError.Should().Contain("Extracting workload").And.NotContain("Error:");
        Directory.Exists(workloads.GetInstallDirectory(PackageId, "1.0.0")).Should().BeTrue();
        byte[] registryBefore = await File.ReadAllBytesAsync(workloads.WorkloadRegistryPath);

        var repeat = await RunAsync(["workload", "install", oldSource, "--allow-untrusted"]);
        repeat.ExitCode.Should().Be(0);
        repeat.StandardError.Should().Contain("already installed");

        var rejectedUpdate = await RunAsync(["workload", "update", PackageId, "--source", catalog.Source]);
        rejectedUpdate.ExitCode.Should().Be(1);
        (rejectedUpdate.StandardOutput + rejectedUpdate.StandardError).Should().Contain("publisher verification failed");
        (await File.ReadAllBytesAsync(workloads.WorkloadRegistryPath)).Should().Equal(registryBefore);
        Directory.Exists(workloads.GetInstallDirectory(PackageId, "1.0.0")).Should().BeTrue();
        Directory.Exists(workloads.GetInstallDirectory(PackageId, "1.1.0")).Should().BeFalse();

        var updated = await RunAsync(["workload", "update", PackageId, "--source", catalog.Source, "--allow-untrusted"]);
        updated.ExitCode.Should().Be(0, updated.StandardOutput + updated.StandardError);
        updated.StandardError.Should().NotContain("Error:");
        Directory.Exists(workloads.GetInstallDirectory(PackageId, "1.1.0")).Should().BeTrue();
        Directory.Exists(workloads.GetInstallDirectory(PackageId, "1.0.0")).Should().BeFalse();

        var reused = await RunAsync(["workload", "update", PackageId, "--source", catalog.Source]);
        reused.ExitCode.Should().Be(0);
        reused.StandardError.Should().Contain("latest available version (1.1.0)");
        (await File.ReadAllBytesAsync(oldSource)).Should().Equal(oldBytes);
        (await File.ReadAllBytesAsync(newSource)).Should().Equal(newBytes);
        Directory.GetDirectories(Path.Combine(Root, CliProcessTestRunner.StateDirectoryName), "func-workload-trust-*", SearchOption.AllDirectories)
            .Should().BeEmpty();

        Task<CliProcessTestRunner.CliResult> RunAsync(string[] args)
            => runner.RunAsync(Root, paths.Home, workloads.Home, Root, args, cancellation.Token);
    }

    private sealed class LoopbackNuGetFeed : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Dictionary<string, byte[]> _responses;
        private readonly Task _server;

        public LoopbackNuGetFeed(string packageId, byte[] package)
        {
            _listener.Start();
            string address = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            Source = address + "index.json";
            _responses = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["/index.json"] = Encoding.UTF8.GetBytes(
                    $$"""{"version":"3.0.0","resources":[{"@id":"{{address}}flat/","@type":"PackageBaseAddress/3.0.0"}]}"""),
                [$"/flat/{packageId}/index.json"] = Encoding.UTF8.GetBytes("""{"versions":["1.0.0","1.1.0"]}"""),
                [$"/flat/{packageId}/1.1.0/{packageId}.1.1.0.nupkg"] = package,
            };
            _server = ServeAsync(_stop.Token);
        }

        public string Source { get; }

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
            }
            finally
            {
                _stop.Dispose();
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

                bool found = _responses.TryGetValue(request.Split(' ')[1], out byte[]? body);
                body ??= [];
                string headers = $"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken);
                await stream.WriteAsync(body, cancellationToken);
            }
        }
    }
}
