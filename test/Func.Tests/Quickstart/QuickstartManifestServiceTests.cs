// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using Azure.Functions.Cli.Quickstart;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Quickstart;

public sealed class QuickstartManifestServiceTests
{
    private readonly QuickstartManifestOptions _options = new()
    {
        ManifestUrl = "https://cdn.functions.azure.com/test/manifest.json",
        CacheDirectory = "/tmp/quickstart-test",
        CacheTtl = TimeSpan.FromHours(24),
    };

    [Fact]
    public async Task GetManifestAsync_FetchesFromCdn_WhenNoCacheExists()
    {
        string manifest = CreateManifestJson("http-python", "Python", "http", "v1.0.0");
        IManifestCache cache = CreateCache(manifestJson: null, meta: null);
        QuickstartManifestService service = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifest),
                Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"etag1\"") },
            },
            cache);

        QuickstartManifest result = await service.GetManifestAsync();

        Assert.Single(result.Entries);
        Assert.Equal("http-python", result.Entries[0].Id);
        cache.Received(1).WriteManifest(manifest);
        cache.Received(1).WriteMeta(Arg.Any<ManifestCacheMeta>());
    }

    [Fact]
    public async Task GetManifestAsync_UsesCachedManifest_WhenCacheIsFresh()
    {
        string manifest = CreateManifestJson("cached-entry", "Python", "http", "v1.0.0");
        ManifestCacheMeta meta = new("\"etag1\"", DateTimeOffset.UtcNow);
        IManifestCache cache = CreateCache(manifestJson: manifest, meta: meta);

        QuickstartManifestService service = CreateService(
            new HttpResponseMessage(HttpStatusCode.InternalServerError), cache);

        QuickstartManifest result = await service.GetManifestAsync();

        Assert.Single(result.Entries);
        Assert.Equal("cached-entry", result.Entries[0].Id);
    }

    [Fact]
    public async Task GetManifestAsync_RefreshesCacheTimestamp_On304()
    {
        string manifest = CreateManifestJson("existing-entry", "Python", "http", "v1.0.0");
        ManifestCacheMeta staleMeta = new("\"etag1\"", DateTimeOffset.UtcNow.AddHours(-25));
        IManifestCache cache = CreateCache(manifestJson: manifest, meta: staleMeta);

        QuickstartManifestService service = CreateService(
            new HttpResponseMessage(HttpStatusCode.NotModified), cache);

        QuickstartManifest result = await service.GetManifestAsync();

        Assert.Single(result.Entries);
        Assert.Equal("existing-entry", result.Entries[0].Id);
        cache.Received(1).WriteMeta(Arg.Is<ManifestCacheMeta>(m => m.ETag == "\"etag1\""));
    }

    [Fact]
    public async Task GetManifestAsync_FallsBackToStaleCache_OnNetworkFailure()
    {
        string manifest = CreateManifestJson("stale-entry", "Python", "http", "v1.0.0");
        ManifestCacheMeta staleMeta = new("\"etag1\"", DateTimeOffset.UtcNow.AddHours(-25));
        IManifestCache cache = CreateCache(manifestJson: manifest, meta: staleMeta);

        QuickstartManifestService service = CreateService(
            new HttpRequestException("Network unreachable"), cache);

        QuickstartManifest result = await service.GetManifestAsync();

        Assert.Single(result.Entries);
        Assert.Equal("stale-entry", result.Entries[0].Id);
    }

    [Fact]
    public async Task GetManifestAsync_Throws_WhenNoNetworkAndNoCache()
    {
        IManifestCache cache = CreateCache(manifestJson: null, meta: null);

        QuickstartManifestService service = CreateService(
            new HttpRequestException("Network unreachable"), cache);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetManifestAsync());
    }

    [Fact]
    public async Task GetManifestAsync_FiltersOutEntriesWithoutGitRef()
    {
        string manifest = CreateManifestJsonRaw(
        [
            CreateEntryJson("has-ref", "Python", "http", "v1.0.0"),
            CreateEntryJson("no-ref", "Python", "http", null),
        ]);
        IManifestCache cache = CreateCache(manifestJson: null, meta: null);

        QuickstartManifestService service = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest) }, cache);

        QuickstartManifest result = await service.GetManifestAsync();

        Assert.Single(result.Entries);
        Assert.Equal("has-ref", result.Entries[0].Id);
    }

    [Fact]
    public async Task GetManifestAsync_FiltersOutEntriesWithNonVPrefixedGitRef()
    {
        string manifest = CreateManifestJsonRaw(
        [
            CreateEntryJson("tagged", "Python", "http", "v1.0.0"),
            CreateEntryJson("branch", "Python", "http", "main"),
        ]);
        IManifestCache cache = CreateCache(manifestJson: null, meta: null);

        QuickstartManifestService service = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest) }, cache);

        QuickstartManifest result = await service.GetManifestAsync();

        Assert.Single(result.Entries);
        Assert.Equal("tagged", result.Entries[0].Id);
    }

    [Fact]
    public async Task GetManifestAsync_FiltersOutUntrustedRepoUrls()
    {
        string json = """
            { "templates": [
                { "id": "trusted", "displayName": "Trusted", "language": "Python", "resource": "http",
                  "repositoryUrl": "https://github.com/Azure-Samples/test-repo", "folderPath": ".", "gitRef": "v1.0.0",
                  "priority": 100 },
                { "id": "untrusted", "displayName": "Untrusted", "language": "Python", "resource": "http",
                  "repositoryUrl": "https://github.com/random-user/test-repo", "folderPath": ".", "gitRef": "v1.0.0",
                  "priority": 100 }
            ]}
            """;
        IManifestCache cache = CreateCache(manifestJson: null, meta: null);

        QuickstartManifestService service = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }, cache);

        QuickstartManifest result = await service.GetManifestAsync();

        Assert.Single(result.Entries);
        Assert.Equal("trusted", result.Entries[0].Id);
    }

    [Fact]
    public async Task GetManifestAsync_DoesNotFilterByLanguage()
    {
        string manifest = CreateManifestJsonRaw(
        [
            CreateEntryJson("python-entry", "Python", "http", "v1.0.0"),
            CreateEntryJson("bicep-entry", "Bicep", "http", "v1.0.0"),
            CreateEntryJson("terraform-entry", "Terraform", "http", "v1.0.0"),
        ]);
        IManifestCache cache = CreateCache(manifestJson: null, meta: null);

        QuickstartManifestService service = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest) }, cache);

        QuickstartManifest result = await service.GetManifestAsync();

        Assert.Equal(3, result.Entries.Count);
    }

    [Fact]
    public async Task GetManifestAsync_FallsBackToCache_WhenResponseIsMalformed()
    {
        string json = """[ { "id": "bare-entry" } ]""";
        string cachedManifest = CreateManifestJson("cached", "Python", "http", "v1.0.0");
        ManifestCacheMeta meta = new("\"etag1\"", DateTimeOffset.UtcNow.AddHours(-25));
        IManifestCache cache = CreateCache(manifestJson: cachedManifest, meta: meta);

        QuickstartManifestService service = CreateService(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }, cache);

        QuickstartManifest result = await service.GetManifestAsync();

        Assert.Single(result.Entries);
        Assert.Equal("cached", result.Entries[0].Id);
    }

    [Fact]
    public async Task GetManifestAsync_LoadsFromLocalFile_WhenOverrideIsFilePath()
    {
        string manifest = CreateManifestJson("local-entry", "Python", "http", "v1.0.0");
        string tempFile = Path.Combine(Path.GetTempPath(), $"test-manifest-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(tempFile, manifest);
            IManifestCache cache = Substitute.For<IManifestCache>();

            _options.ManifestUrl = tempFile;
            QuickstartManifestService service = CreateService(
                new HttpResponseMessage(HttpStatusCode.InternalServerError), cache);

            QuickstartManifest result = await service.GetManifestAsync();

            Assert.Single(result.Entries);
            Assert.Equal("local-entry", result.Entries[0].Id);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task GetManifestAsync_Throws_WhenLocalFileDoesNotExist()
    {
        IManifestCache cache = Substitute.For<IManifestCache>();

        _options.ManifestUrl = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid()}.json");
        QuickstartManifestService service = CreateService(
            new HttpResponseMessage(HttpStatusCode.InternalServerError), cache);

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.GetManifestAsync());
    }

    [Fact]
    public async Task GetManifestAsync_Throws_WhenLocalFileContainsMalformedJson()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"test-manifest-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(tempFile, "{ not valid json }}}");
            IManifestCache cache = Substitute.For<IManifestCache>();

            _options.ManifestUrl = tempFile;
            QuickstartManifestService service = CreateService(
                new HttpResponseMessage(HttpStatusCode.InternalServerError), cache);

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetManifestAsync());

            Assert.Contains("empty or malformed", ex.Message);
            Assert.IsType<System.Text.Json.JsonException>(ex.InnerException);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private QuickstartManifestService CreateService(HttpResponseMessage response, IManifestCache cache)
    {
        var handler = new FakeHttpMessageHandler(response);
        return CreateServiceFromHandler(handler, cache);
    }

    private QuickstartManifestService CreateService(Exception exception, IManifestCache cache)
    {
        var handler = new FakeHttpMessageHandler(exception);
        return CreateServiceFromHandler(handler, cache);
    }

    private QuickstartManifestService CreateServiceFromHandler(FakeHttpMessageHandler handler, IManifestCache cache)
    {
        HttpClient httpClient = new(handler) { BaseAddress = new Uri("https://cdn.functions.azure.com") };
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(QuickstartRegistration.HttpClientName).Returns(httpClient);
        IOptions<QuickstartManifestOptions> opts = Options.Create(_options);

        return new QuickstartManifestService(factory, cache, opts, TimeProvider.System,
            NullLogger<QuickstartManifestService>.Instance);
    }

    private static IManifestCache CreateCache(string? manifestJson, ManifestCacheMeta? meta)
    {
        IManifestCache cache = Substitute.For<IManifestCache>();
        cache.TryReadMeta().Returns(meta);
        cache.TryReadManifest().Returns(manifestJson);
        cache.ManifestExists().Returns(manifestJson is not null);
        return cache;
    }

    private static string CreateManifestJson(string id, string language, string resource, string gitRef)
    {
        return CreateManifestJsonRaw([CreateEntryJson(id, language, resource, gitRef)]);
    }

    private static string CreateManifestJsonRaw(string[] entries)
    {
        return $$"""{ "templates": [{{string.Join(",", entries)}}] }""";
    }

    private static string CreateEntryJson(string id, string language, string resource, string? gitRef)
    {
        string gitRefPart = gitRef is not null ? $""", "gitRef": "{gitRef}" """ : "";
        return $$"""
            { "id": "{{id}}", "displayName": "{{id}}", "language": "{{language}}",
              "resource": "{{resource}}", "repositoryUrl": "https://github.com/Azure-Samples/test-repo",
              "folderPath": ".", "priority": 100{{gitRefPart}} }
            """;
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _response;
        private readonly Exception? _exception;

        public FakeHttpMessageHandler(HttpResponseMessage response) => _response = response;

        public FakeHttpMessageHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_exception is not null)
            {
                return Task.FromException<HttpResponseMessage>(_exception);
            }

            return Task.FromResult(_response!);
        }
    }
}
