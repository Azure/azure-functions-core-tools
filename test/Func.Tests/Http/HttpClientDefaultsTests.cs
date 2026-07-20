// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Tests.Http;

public sealed class HttpClientDefaultsTests
{
    [Fact]
    public async Task AddCliHttpDefaults_SetsUserAgentOnAllClients()
    {
        string? capturedUserAgent = null;
        var handler = new CapturingHttpMessageHandler(request =>
        {
            capturedUserAgent = request.Headers.UserAgent.ToString();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });

        ServiceCollection services = new();
        services.AddCliHttpDefaults();
        services.AddHttpClient("TestClient")
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using ServiceProvider provider = services.BuildServiceProvider();
        IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();
        HttpClient client = factory.CreateClient("TestClient");

        await client.GetAsync("https://example.com");

        capturedUserAgent.Should().NotBeNull();
        capturedUserAgent.Should().StartWith("AzureFunctionsCli/");
        capturedUserAgent.Should().Contain("(");
    }

    [Fact]
    public async Task AddCliHttpDefaults_AppliesUserAgentToUnnamedClients()
    {
        string? capturedUserAgent = null;
        var handler = new CapturingHttpMessageHandler(request =>
        {
            capturedUserAgent = request.Headers.UserAgent.ToString();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });

        ServiceCollection services = new();
        services.AddCliHttpDefaults();
        services.AddHttpClient(string.Empty)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using ServiceProvider provider = services.BuildServiceProvider();
        IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();
        HttpClient client = factory.CreateClient(string.Empty);

        await client.GetAsync("https://example.com");

        capturedUserAgent.Should().NotBeNull();
        capturedUserAgent.Should().StartWith("AzureFunctionsCli/");
    }

    [Fact]
    public async Task AddCliHttpDefaults_UserAgentIncludesVersionAndPlatform()
    {
        string? capturedUserAgent = null;
        var handler = new CapturingHttpMessageHandler(request =>
        {
            capturedUserAgent = request.Headers.UserAgent.ToString();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });

        ServiceCollection services = new();
        services.AddCliHttpDefaults();
        services.AddHttpClient("VersionCheck")
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using ServiceProvider provider = services.BuildServiceProvider();
        IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();
        HttpClient client = factory.CreateClient("VersionCheck");

        await client.GetAsync("https://example.com");

        capturedUserAgent.Should().NotBeNull();
        // Format: AzureFunctionsCli/{version} ({os})
        capturedUserAgent.Should().MatchRegex(@"^AzureFunctionsCli/\S+ \(.+\)$");
    }

    private sealed class CapturingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
