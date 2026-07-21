// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        var httpClient = CreateHttpClient(handler);
        var fileSystem = new InMemoryProfileFileSystem();
        var configPaths = new CliConfigurationPathsOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var source = new RemoteProfileSource(httpClient, Options.Create(new RemoteProfileOptions()), new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);

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
        var httpClient = CreateHttpClient(handler);
        var fileSystem = new InMemoryProfileFileSystem();
        var configPaths = new CliConfigurationPathsOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var source = new RemoteProfileSource(httpClient, Options.Create(new RemoteProfileOptions()), new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);

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
        var httpClient = CreateHttpClient(handler);
        var source = new RemoteProfileSource(httpClient, Options.Create(new RemoteProfileOptions()), new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);

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
        var httpClient = CreateHttpClient(handler);
        var source = new RemoteProfileSource(httpClient, Options.Create(new RemoteProfileOptions()), new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);

        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));
        ProfileSourceSnapshot snapshot = await source.LoadAsync(context, CancellationToken.None);

        Assert.Contains("flex", snapshot.Profiles.Keys);
        Assert.Contains("stale", snapshot.Source.DisplayName);
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmptyWhenNoRemoteOrCache()
    {
        var handler = new FakeHttpMessageHandler(null!, null!, shouldFail: true);
        var httpClient = CreateHttpClient(handler);
        var fileSystem = new InMemoryProfileFileSystem();
        var configPaths = new CliConfigurationPathsOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var source = new RemoteProfileSource(httpClient, Options.Create(new RemoteProfileOptions()), new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);

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
        var httpClient = CreateHttpClient(handler);
        var source = new RemoteProfileSource(httpClient, Options.Create(new RemoteProfileOptions()), new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);

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
        var httpClient = CreateHttpClient(handler);
        var fileSystem = new InMemoryProfileFileSystem();
        var configPaths = new CliConfigurationPathsOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var source = new RemoteProfileSource(httpClient, Options.Create(new RemoteProfileOptions()), new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);

        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.LoadAsync(context, cts.Token));
    }

    [Fact]
    public async Task LoadAsync_ChecksumValidButMalformedRemote_ReturnsEmptyAndDoesNotCache()
    {
        // Content that hashes fine but is not a valid profile registry
        string malformedJson = """{ "not": "a registry" }""";
        string checksum = ComputeSha256(malformedJson);
        var handler = new FakeHttpMessageHandler(malformedJson, checksum);
        var httpClient = CreateHttpClient(handler);
        var fileSystem = new InMemoryProfileFileSystem();
        var configPaths = new CliConfigurationPathsOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var source = new RemoteProfileSource(httpClient, Options.Create(new RemoteProfileOptions()), new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);

        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));
        ProfileSourceSnapshot snapshot = await source.LoadAsync(context, CancellationToken.None);

        // Should fall through to empty, not throw
        Assert.Empty(snapshot.Profiles);

        // Should not have persisted the malformed content to cache
        string cachePath = Path.Combine(configPaths.Home, "profiles", "registry.json");
        Assert.False(fileSystem.Files.ContainsKey(cachePath));
    }

    [Fact]
    public async Task LoadAsync_ChecksumValidButMalformedCache_InvalidatesAndReturnsEmpty()
    {
        // Content that hashes fine but is not a valid profile registry
        string malformedJson = """{ "bogus": true }""";
        string checksum = ComputeSha256(malformedJson);
        var fileSystem = new InMemoryProfileFileSystem();
        var configPaths = new CliConfigurationPathsOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        // Pre-populate cache with checksum-valid but malformed content
        string cacheDir = Path.Combine(configPaths.Home, "profiles");
        string cachePath = Path.Combine(cacheDir, "registry.json");
        string checksumPath = Path.Combine(cacheDir, "registry.json.sha256");
        string metaPath = Path.Combine(cacheDir, "registry.json.meta");
        fileSystem.Files[cachePath] = malformedJson;
        fileSystem.Files[checksumPath] = checksum;
        fileSystem.Files[metaPath] = DateTimeOffset.UtcNow.ToString("O");

        // Remote also fails
        var handler = new FakeHttpMessageHandler(null!, null!, shouldFail: true);
        var httpClient = CreateHttpClient(handler);
        var source = new RemoteProfileSource(httpClient, Options.Create(new RemoteProfileOptions()), new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);

        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));
        ProfileSourceSnapshot snapshot = await source.LoadAsync(context, CancellationToken.None);

        // Should not throw, should return empty
        Assert.Empty(snapshot.Profiles);

        // Cache should be invalidated so it doesn't poison future runs
        Assert.False(fileSystem.Files.ContainsKey(cachePath));
        Assert.False(fileSystem.Files.ContainsKey(checksumPath));
        Assert.False(fileSystem.Files.ContainsKey(metaPath));
    }

    [Fact]
    public async Task LoadAsync_MalformedCacheDoesNotPoisonSecondCall()
    {
        string malformedJson = """{ "not": "valid" }""";
        string malformedChecksum = ComputeSha256(malformedJson);
        var fileSystem = new InMemoryProfileFileSystem();
        var configPaths = new CliConfigurationPathsOptions(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        // Pre-populate cache with malformed content
        string cacheDir = Path.Combine(configPaths.Home, "profiles");
        fileSystem.Files[Path.Combine(cacheDir, "registry.json")] = malformedJson;
        fileSystem.Files[Path.Combine(cacheDir, "registry.json.sha256")] = malformedChecksum;
        fileSystem.Files[Path.Combine(cacheDir, "registry.json.meta")] = DateTimeOffset.UtcNow.ToString("O");

        // First call: remote fails, cache is malformed → empty + cache invalidated
        var failHandler = new FakeHttpMessageHandler(null!, null!, shouldFail: true);
        var httpClient1 = CreateHttpClient(failHandler);
        var source1 = new RemoteProfileSource(httpClient1, Options.Create(new RemoteProfileOptions()), new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);
        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));
        ProfileSourceSnapshot first = await source1.LoadAsync(context, CancellationToken.None);
        Assert.Empty(first.Profiles);

        // Second call: remote succeeds with valid content → works
        string validChecksum = ComputeSha256(_validRegistry);
        var successHandler = new FakeHttpMessageHandler(_validRegistry, validChecksum);
        var httpClient2 = CreateHttpClient(successHandler);
        var source2 = new RemoteProfileSource(httpClient2, Options.Create(new RemoteProfileOptions()), new ProfileDocumentParser(), fileSystem, configPaths, NullLogger<RemoteProfileSource>.Instance);
        ProfileSourceSnapshot second = await source2.LoadAsync(context, CancellationToken.None);

        Assert.Contains("flex", second.Profiles.Keys);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler) { BaseAddress = new Uri("https://cdn.functions.azure.com/public/") };
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

        public Task DeleteIfExistsAsync(string path)
        {
            Files.Remove(path);
            return Task.CompletedTask;
        }
    }
}
