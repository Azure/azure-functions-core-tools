// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Profiles;

public class RemoteProfileSourceTests
{
    private static readonly string _validRegistry = """
        {
          "$schema": "https://aka.ms/func-profiles/v1/schema.json",
          "generatedAt": "2026-05-23T00:00:00Z",
          "profiles": {
            "flex": {
              "sku": "flex-consumption",
              "status": "stable",
              "host": { "version": "[1.8.1, 4.1048.200)" },
              "extensionBundle": { "version": "[3.0.0, 5.0.0)" },
              "workers": { "node": { "version": "[3.13.0]" } },
              "supportedRuntimes": ["node", "python"]
            }
          }
        }
        """;

    private static string ComputeSha256(string content)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(hash);
    }

    [Fact]
    public async Task LoadAsync_FetchesFromRemoteAndCaches()
    {
        string checksum = ComputeSha256(_validRegistry);
        var handler = new FakeHttpMessageHandler(_validRegistry, checksum);
        var httpClientFactory = CreateFactory(handler);
        var fileSystem = new InMemoryProfileFileSystem();
        var configPaths = new CliConfigurationPathsOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var source = new RemoteProfileSource(httpClientFactory, new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);

        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));
        ProfileSourceSnapshot snapshot = await source.LoadAsync(context, CancellationToken.None);

        Assert.Equal(ProfileSourceKind.BuiltIn, snapshot.Source.Kind);
        Assert.Contains("flex", snapshot.Profiles.Keys);
        Assert.Equal("stable", snapshot.Profiles["flex"].Status);

        // Verify cache was written
        string cachePath = Path.Combine(configPaths.Home, "profiles", "registry.json");
        Assert.True(fileSystem.Files.ContainsKey(cachePath));
    }

    [Fact]
    public async Task LoadAsync_RejectsChecksumMismatch_ReturnsEmpty()
    {
        var handler = new FakeHttpMessageHandler(_validRegistry, "badhash");
        var httpClientFactory = CreateFactory(handler);
        var fileSystem = new InMemoryProfileFileSystem();
        var configPaths = new CliConfigurationPathsOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var source = new RemoteProfileSource(httpClientFactory, new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);

        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));
        ProfileSourceSnapshot snapshot = await source.LoadAsync(context, CancellationToken.None);

        Assert.Empty(snapshot.Profiles);
    }

    [Fact]
    public async Task LoadAsync_UsesCacheWithinTtl()
    {
        string checksum = ComputeSha256(_validRegistry);
        var fileSystem = new InMemoryProfileFileSystem();
        var configPaths = new CliConfigurationPathsOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        // Pre-populate cache
        string cacheDir = Path.Combine(configPaths.Home, "profiles");
        fileSystem.Files[Path.Combine(cacheDir, "registry.json")] = _validRegistry;
        fileSystem.Files[Path.Combine(cacheDir, "registry.json.sha256")] = checksum;
        fileSystem.Files[Path.Combine(cacheDir, "registry.json.meta")] = DateTimeOffset.UtcNow.ToString("O");

        // Handler that would fail if called
        var handler = new FakeHttpMessageHandler(null!, null!, shouldFail: true);
        var httpClientFactory = CreateFactory(handler);
        var source = new RemoteProfileSource(httpClientFactory, new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);

        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));
        ProfileSourceSnapshot snapshot = await source.LoadAsync(context, CancellationToken.None);

        Assert.Contains("flex", snapshot.Profiles.Keys);
    }

    [Fact]
    public async Task LoadAsync_FallsBackToStaleCacheOnNetworkFailure()
    {
        string checksum = ComputeSha256(_validRegistry);
        var fileSystem = new InMemoryProfileFileSystem();
        var configPaths = new CliConfigurationPathsOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        // Pre-populate stale cache (2 hours old, past TTL but under 7 days)
        string cacheDir = Path.Combine(configPaths.Home, "profiles");
        fileSystem.Files[Path.Combine(cacheDir, "registry.json")] = _validRegistry;
        fileSystem.Files[Path.Combine(cacheDir, "registry.json.sha256")] = checksum;
        fileSystem.Files[Path.Combine(cacheDir, "registry.json.meta")] = DateTimeOffset.UtcNow.AddHours(-2).ToString("O");

        var handler = new FakeHttpMessageHandler(null!, null!, shouldFail: true);
        var httpClientFactory = CreateFactory(handler);
        var source = new RemoteProfileSource(httpClientFactory, new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);

        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));
        ProfileSourceSnapshot snapshot = await source.LoadAsync(context, CancellationToken.None);

        Assert.Contains("flex", snapshot.Profiles.Keys);
        Assert.Contains("stale", snapshot.Source.DisplayName);
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmptyWhenNoRemoteOrCache()
    {
        var handler = new FakeHttpMessageHandler(null!, null!, shouldFail: true);
        var httpClientFactory = CreateFactory(handler);
        var fileSystem = new InMemoryProfileFileSystem();
        var configPaths = new CliConfigurationPathsOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var source = new RemoteProfileSource(httpClientFactory, new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);

        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));
        ProfileSourceSnapshot snapshot = await source.LoadAsync(context, CancellationToken.None);

        Assert.Empty(snapshot.Profiles);
    }

    [Fact]
    public async Task LoadAsync_DiscardsCorruptCacheWithinTtl()
    {
        var fileSystem = new InMemoryProfileFileSystem();
        var configPaths = new CliConfigurationPathsOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        // Pre-populate cache with mismatched checksum
        string cacheDir = Path.Combine(configPaths.Home, "profiles");
        fileSystem.Files[Path.Combine(cacheDir, "registry.json")] = _validRegistry;
        fileSystem.Files[Path.Combine(cacheDir, "registry.json.sha256")] = "corrupted_checksum_value";
        fileSystem.Files[Path.Combine(cacheDir, "registry.json.meta")] = DateTimeOffset.UtcNow.ToString("O");

        // Remote also fails — should end up empty (no valid cache, no remote)
        var handler = new FakeHttpMessageHandler(null!, null!, shouldFail: true);
        var httpClientFactory = CreateFactory(handler);
        var source = new RemoteProfileSource(httpClientFactory, new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);

        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));
        ProfileSourceSnapshot snapshot = await source.LoadAsync(context, CancellationToken.None);

        Assert.Empty(snapshot.Profiles);
    }

    [Fact]
    public async Task LoadAsync_PropagatesUserCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new FakeHttpMessageHandler(_validRegistry, ComputeSha256(_validRegistry));
        var httpClientFactory = CreateFactory(handler);
        var fileSystem = new InMemoryProfileFileSystem();
        var configPaths = new CliConfigurationPathsOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var source = new RemoteProfileSource(httpClientFactory, new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);

        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.LoadAsync(context, cts.Token));
    }

    private static IHttpClientFactory CreateFactory(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(RemoteProfileSource.HttpClientName)
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("https://cdn.functions.azure.com/public/") });
        return factory;
    }

    private sealed class FakeHttpMessageHandler(string? registryContent, string? checksumContent, bool shouldFail = false)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (shouldFail)
            {
                throw new HttpRequestException("Simulated network failure");
            }

            string content = request.RequestUri!.AbsoluteUri.Contains("sha256")
                ? checksumContent!
                : registryContent!;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }

    private sealed class InMemoryProfileFileSystem : IProfileFileSystem
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken)
        {
            return Task.FromResult(Files.TryGetValue(path, out string? content) ? content : null);
        }

        public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
        {
            Files[path] = contents;
            return Task.CompletedTask;
        }

        public Task WriteAllTextAtomicAsync(string path, string contents, CancellationToken cancellationToken)
        {
            Files[path] = contents;
            return Task.CompletedTask;
        }
    }
}
