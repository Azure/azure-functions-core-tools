// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Packages;

namespace Azure.Functions.Cli.Tests.Templates.Packages;

public sealed class TemplateSearchTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("http")]
    public async Task SearchAsync_StubReportsUnavailableInsteadOfReturningEmptyResults(string? query)
    {
        var search = new TemplateSearch();

        Func<Task> act = () => search.SearchAsync(query, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*CDN-backed template search is not yet available*");
    }

    [Fact]
    public async Task SearchAsync_CancelledTokenHonorsCancellation()
    {
        var search = new TemplateSearch();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = () => search.SearchAsync(null, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
