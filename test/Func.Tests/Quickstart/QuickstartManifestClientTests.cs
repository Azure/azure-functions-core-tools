// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Quickstart;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.Cli.Tests.Quickstart;

public sealed class QuickstartManifestClientTests : IDisposable
{
    private readonly string _cacheDir;

    public QuickstartManifestClientTests()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), $"func-quickstart-cache-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
    }

    // --- Constructor validation --------------------------------------------

    [Fact]
    public void Constructor_NullHttpClient_Throws()
    {
        var options = Options.Create(new QuickstartManifestOptions());
        Assert.Throws<ArgumentNullException>(() =>
            new QuickstartManifestClient(null!, options, NullLogger<QuickstartManifestClient>.Instance));
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new QuickstartManifestClient(new HttpClient(), null!, NullLogger<QuickstartManifestClient>.Instance));
    }

    // --- Successful fetch from CDN -----------------------------------------

    [Fact]
    public async Task GetManifestAsync_HttpSuccess_ReturnsEntries()
    {
        string json = BuildEnvelope(
            Entry("entry-a", "Python", "HTTP Trigger"),
            Entry("entry-b", "CSharp", "Timer Trigger"));

        var client = CreateClient(json, HttpStatusCode.OK, etag: "\"v1\"");
        var manifest = await client.GetManifestAsync(CancellationToken.None);

        Assert.Equal(2, manifest.Entries.Count);
        Assert.Contains(manifest.Entries, e => e.Id == "entry-a");
        Assert.Contains(manifest.Entries, e => e.Id == "entry-b");
    }

    [Fact]
    public async Task GetManifestAsync_WritesCacheFiles()
    {
        string json = BuildEnvelope(Entry("a", "Python", "HTTP Trigger"));
        var client = CreateClient(json, HttpStatusCode.OK, etag: "\"v1\"");

        _ = await client.GetManifestAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_cacheDir, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(_cacheDir, "manifest-meta.json")));
    }

    // --- Filtering at deserialization --------------------------------------

    [Fact]
    public async Task GetManifestAsync_FiltersOutIacOnlyLanguages()
    {
        string json = BuildEnvelope(
            Entry("py", "Python", "HTTP Trigger"),
            Entry("bicep", "Bicep", "Infrastructure"),
            Entry("terra", "Terraform", "Infrastructure"),
            Entry("arm", "ARM", "Infrastructure"));

        var client = CreateClient(json, HttpStatusCode.OK, etag: null);
        var manifest = await client.GetManifestAsync(CancellationToken.None);

        Assert.Single(manifest.Entries);
        Assert.Equal("py", manifest.Entries[0].Id);
    }

    [Fact]
    public async Task GetManifestAsync_FiltersOutUntrustedRepoUrls()
    {
        string json = BuildEnvelope(
            Entry("ok", "Python", "HTTP", "https://github.com/Azure/repo"),
            Entry("bad", "Python", "HTTP", "https://github.com/attacker/repo"));

        var client = CreateClient(json, HttpStatusCode.OK, etag: null);
        var manifest = await client.GetManifestAsync(CancellationToken.None);

        Assert.Single(manifest.Entries);
        Assert.Equal("ok", manifest.Entries[0].Id);
    }

    [Fact]
    public async Task GetManifestAsync_DropsEntriesMissingRequiredFields()
    {
        string json = BuildEnvelope(
            Entry("good", "Python", "HTTP Trigger"),
            Entry("", "Python", "HTTP Trigger"),               // missing id
            Entry("no-lang", "", "HTTP Trigger"),               // missing language
            Entry("no-resource", "Python", ""));                // missing resource

        var client = CreateClient(json, HttpStatusCode.OK, etag: null);
        var manifest = await client.GetManifestAsync(CancellationToken.None);

        Assert.Single(manifest.Entries);
        Assert.Equal("good", manifest.Entries[0].Id);
    }

    // --- Caching / ETag behaviour ------------------------------------------

    [Fact]
    public async Task GetManifestAsync_NotModified304_ReturnsCachedManifest()
    {
        // Pre-seed the cache.
        Directory.CreateDirectory(_cacheDir);
        string cachedJson = BuildEnvelope(Entry("cached", "Python", "HTTP Trigger"));
        File.WriteAllText(Path.Combine(_cacheDir, "manifest.json"), cachedJson);

        // Meta with a stale CachedAt so a 304 path runs (not the fresh-cache path).
        var meta = new
        {
            ETag = "\"v1\"",
            CachedAt = DateTimeOffset.UtcNow.AddDays(-2),
        };
        File.WriteAllText(Path.Combine(_cacheDir, "manifest-meta.json"), JsonSerializer.Serialize(meta));

        var client = CreateClient(body: string.Empty, HttpStatusCode.NotModified, etag: "\"v1\"");
        var manifest = await client.GetManifestAsync(CancellationToken.None);

        Assert.Single(manifest.Entries);
        Assert.Equal("cached", manifest.Entries[0].Id);
    }

    [Fact]
    public async Task GetManifestAsync_FreshCache_UsesCachedCopyWithoutHttp()
    {
        // Pre-seed a fresh cache (CachedAt = now).
        Directory.CreateDirectory(_cacheDir);
        string cachedJson = BuildEnvelope(Entry("from-cache", "Python", "HTTP Trigger"));
        File.WriteAllText(Path.Combine(_cacheDir, "manifest.json"), cachedJson);
        var meta = new { ETag = "\"v1\"", CachedAt = DateTimeOffset.UtcNow };
        File.WriteAllText(Path.Combine(_cacheDir, "manifest-meta.json"), JsonSerializer.Serialize(meta));

        // The HTTP seam will throw if it's invoked — proving the cache path is used.
        var client = new ThrowingClient(_cacheDir);
        var manifest = await client.GetManifestAsync(CancellationToken.None);

        Assert.Single(manifest.Entries);
        Assert.Equal("from-cache", manifest.Entries[0].Id);
    }

    [Fact]
    public async Task GetManifestAsync_NetworkFailureWithCachedCopy_FallsBackToCache()
    {
        // Stale cache (so the fresh-cache fast path doesn't kick in).
        Directory.CreateDirectory(_cacheDir);
        string cachedJson = BuildEnvelope(Entry("stale", "Python", "HTTP Trigger"));
        File.WriteAllText(Path.Combine(_cacheDir, "manifest.json"), cachedJson);
        var meta = new { ETag = "\"v1\"", CachedAt = DateTimeOffset.UtcNow.AddDays(-30) };
        File.WriteAllText(Path.Combine(_cacheDir, "manifest-meta.json"), JsonSerializer.Serialize(meta));

        var client = new NetworkFailureClient(_cacheDir);
        var manifest = await client.GetManifestAsync(CancellationToken.None);

        Assert.Single(manifest.Entries);
        Assert.Equal("stale", manifest.Entries[0].Id);
    }

    [Fact]
    public async Task GetManifestAsync_NetworkFailureNoCache_ThrowsInvalidOperation()
    {
        var client = new NetworkFailureClient(_cacheDir);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetManifestAsync(CancellationToken.None));

        Assert.Contains("Unable to fetch the quickstart manifest", ex.Message);
    }

    // --- Local file override ------------------------------------------------

    [Fact]
    public async Task GetManifestAsync_LocalFileOverride_LoadsFromDisk()
    {
        // Write the override file outside the cache dir so the test isn't sensitive
        // to disposal order between the override path and the cache dir.
        string overridePath = Path.Combine(Path.GetTempPath(), $"override-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(overridePath, BuildEnvelope(Entry("local", "Python", "HTTP Trigger")));

            var options = new QuickstartManifestOptions
            {
                ManifestUrl = new Uri(overridePath).AbsoluteUri,
                CacheDirectory = _cacheDir,
            };
            var client = new QuickstartManifestClient(
                new HttpClient(), Options.Create(options),
                NullLogger<QuickstartManifestClient>.Instance);

            var manifest = await client.GetManifestAsync(CancellationToken.None);

            Assert.Single(manifest.Entries);
            Assert.Equal("local", manifest.Entries[0].Id);
        }
        finally
        {
            if (File.Exists(overridePath))
            {
                File.Delete(overridePath);
            }
        }
    }

    [Fact]
    public async Task GetManifestAsync_LocalFileOverride_MissingFile_Throws()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");
        var options = new QuickstartManifestOptions
        {
            ManifestUrl = new Uri(missingPath).AbsoluteUri,
            CacheDirectory = _cacheDir,
        };
        var client = new QuickstartManifestClient(
            new HttpClient(), Options.Create(options),
            NullLogger<QuickstartManifestClient>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetManifestAsync(CancellationToken.None));

        Assert.Contains("does not exist", ex.Message);
    }

    // --- Helpers ------------------------------------------------------------

    private TestManifestClient CreateClient(string body, HttpStatusCode status, string? etag)
    {
        var options = Options.Create(new QuickstartManifestOptions { CacheDirectory = _cacheDir });
        return new TestManifestClient(options, body, status, etag);
    }

    private static string BuildEnvelope(params object[] entries) =>
        JsonSerializer.Serialize(new { templates = entries });

    private static object Entry(
        string id, string language, string resource,
        string repositoryUrl = "https://github.com/Azure/test-repo") => new
        {
            id,
            displayName = id,
            language,
            resource,
            repositoryUrl,
            folderPath = ".",
            gitRef = "v1",
        };

    /// <summary>
    /// Test client whose HTTP seam returns a fixed response.
    /// </summary>
    private sealed class TestManifestClient : QuickstartManifestClient
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;
        private readonly string? _etag;

        public TestManifestClient(
            IOptions<QuickstartManifestOptions> options,
            string body, HttpStatusCode status, string? etag)
            : base(new HttpClient(), options, NullLogger<QuickstartManifestClient>.Instance)
        {
            _body = body;
            _status = status;
            _etag = etag;
        }

        protected override Task<HttpResponseMessage> SendManifestRequestAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
            if (_etag is not null)
            {
                response.Headers.TryAddWithoutValidation("ETag", _etag);
            }

            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Test client whose HTTP seam always throws — used to simulate offline mode.
    /// </summary>
    private sealed class NetworkFailureClient : QuickstartManifestClient
    {
        public NetworkFailureClient(string cacheDir)
            : base(
                new HttpClient(),
                Options.Create(new QuickstartManifestOptions { CacheDirectory = cacheDir }),
                NullLogger<QuickstartManifestClient>.Instance)
        {
        }

        protected override Task<HttpResponseMessage> SendManifestRequestAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("simulated offline");
    }

    /// <summary>
    /// Test client that fails the test if its HTTP seam is invoked — proves the
    /// fresh-cache fast path doesn't hit the network.
    /// </summary>
    private sealed class ThrowingClient : QuickstartManifestClient
    {
        public ThrowingClient(string cacheDir)
            : base(
                new HttpClient(),
                Options.Create(new QuickstartManifestOptions { CacheDirectory = cacheDir }),
                NullLogger<QuickstartManifestClient>.Instance)
        {
        }

        protected override Task<HttpResponseMessage> SendManifestRequestAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HTTP must not be called on fresh-cache path.");
    }
}
