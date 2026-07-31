// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Search;

public class FuncSearchIndexProviderTests : IDisposable
{
    private const string MinimalIndex = """
    { "Version": "2.0", "TemplatePackages": [ { "Name": "Contoso.Templates", "Version": "1.0.0", "Templates": [] } ] }
    """;

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "func-search-tests-" + Path.GetRandomFileName());
    private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();

    public FuncSearchIndexProviderTests() => Directory.CreateDirectory(_tempRoot);

    [Fact]
    public async Task GetIndexAsync_LocalFileOverride_DoesNotTouchTheNetwork()
    {
        string indexPath = Path.Combine(_tempRoot, "index.json");
        File.WriteAllText(indexPath, MinimalIndex);
        FuncSearchIndexProvider provider = CreateProvider(indexUri: indexPath);

        FuncSearchIndex index = await provider.GetIndexAsync(CancellationToken.None);

        index.Packages.Should().ContainSingle().Which.Name.Should().Be("Contoso.Templates");
        _httpClientFactory.DidNotReceiveWithAnyArgs().CreateClient(default!);
    }

    [Fact]
    public async Task GetIndexAsync_FileUriOverride_DoesNotTouchTheNetwork()
    {
        string indexPath = Path.Combine(_tempRoot, "index.json");
        File.WriteAllText(indexPath, MinimalIndex);
        FuncSearchIndexProvider provider = CreateProvider(indexUri: new Uri(indexPath).AbsoluteUri);

        FuncSearchIndex index = await provider.GetIndexAsync(CancellationToken.None);

        index.Packages.Should().ContainSingle();
        _httpClientFactory.DidNotReceiveWithAnyArgs().CreateClient(default!);
    }

    [Fact]
    public async Task GetIndexAsync_LocalFileOverrideMissing_ThrowsActionableFileNotFound()
    {
        string missing = Path.Combine(_tempRoot, "nope.json");
        FuncSearchIndexProvider provider = CreateProvider(indexUri: missing);

        Func<Task> act = () => provider.GetIndexAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<FileNotFoundException>())
            .Which.Message.Should().Contain("FUNC_CLI_TEMPLATE_SEARCH_INDEX");
    }

    [Fact]
    public async Task GetIndexAsync_UnreachableWithNoCache_ThrowsActionableError()
    {
        _httpClientFactory.CreateClient(Arg.Any<string>())
            .Returns(_ => new HttpClient(new ThrowingHttpMessageHandler()));
        FuncSearchIndexProvider provider = CreateProvider(indexUri: "https://index.invalid/search.json");

        Func<Task> act = () => provider.GetIndexAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("FUNC_CLI_TEMPLATE_SEARCH_INDEX");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the hermetic temp dir; a leaked temp folder is harmless.
        }
    }

    private FuncSearchIndexProvider CreateProvider(string indexUri)
    {
        var options = new FuncTemplateSearchOptions
        {
            IndexUri = indexUri,
            CacheDirectory = Path.Combine(_tempRoot, "cache"),
        };

        return new FuncSearchIndexProvider(
            _httpClientFactory,
            Options.Create(options),
            TimeProvider.System,
            Substitute.For<ILogger<FuncSearchIndexProvider>>());
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Simulated network failure.");
    }
}
