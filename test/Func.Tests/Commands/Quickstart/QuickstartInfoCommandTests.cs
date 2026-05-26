// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Invocation;
using Azure.Functions.Cli.Commands.Quickstart;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Quickstart;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Quickstart;

public sealed class QuickstartInfoCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IQuickstartManifestClient _manifestClient = Substitute.For<IQuickstartManifestClient>();

    [Fact]
    public void QuickstartInfoCommand_HasIdArgument()
    {
        var cmd = new QuickstartInfoCommand(_interaction, _manifestClient);
        Assert.Single(cmd.Arguments, a => a.Name == "id");
    }

    [Fact]
    public async Task Info_KnownId_WritesTemplateDetails()
    {
        StubManifest(new QuickstartEntry
        {
            Id = "py-http",
            DisplayName = "Python HTTP Trigger",
            Language = "Python",
            Resource = "HTTP Trigger",
            RepositoryUrl = "https://github.com/Azure/test-repo",
            FolderPath = ".",
            ShortDescription = "A simple HTTP trigger.",
            Tags = ["http", "rest"],
            WhatIsIncluded = ["function.json", "app.py"],
        });

        int exit = await InvokeAsync("py-http");

        Assert.Equal(0, exit);
        Assert.Contains(_interaction.Lines, l => l.Contains("Python HTTP Trigger"));
        Assert.Contains(_interaction.Lines, l => l.Contains("py-http"));
        Assert.Contains(_interaction.Lines, l => l.Contains("A simple HTTP trigger."));
        Assert.Contains(_interaction.Lines, l => l.Contains("function.json"));
    }

    [Fact]
    public async Task Info_KnownId_CaseInsensitiveMatch()
    {
        StubManifest(new QuickstartEntry
        {
            Id = "py-http",
            DisplayName = "Python HTTP",
            Language = "Python",
            Resource = "HTTP Trigger",
            RepositoryUrl = "https://github.com/Azure/test-repo",
            FolderPath = ".",
        });

        int exit = await InvokeAsync("PY-HTTP");
        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task Info_UnknownId_ThrowsGracefulException()
    {
        StubManifest(new QuickstartEntry
        {
            Id = "py-http",
            DisplayName = "Python HTTP",
            Language = "Python",
            Resource = "HTTP Trigger",
            RepositoryUrl = "https://github.com/Azure/test-repo",
            FolderPath = ".",
        });

        var ex = await Assert.ThrowsAsync<GracefulException>(() => InvokeAsync("does-not-exist"));
        Assert.Contains("does-not-exist", ex.Message);
        Assert.Contains("func quickstart list", ex.Message);
    }

    [Fact]
    public async Task Info_ManifestFetchFails_ThrowsGracefulException()
    {
        _manifestClient.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns<QuickstartManifest>(_ => throw new InvalidOperationException("offline"));

        var ex = await Assert.ThrowsAsync<GracefulException>(() => InvokeAsync("anything"));
        Assert.Contains("offline", ex.Message);
    }

    // --- Helpers -------------------------------------------------

    private void StubManifest(params QuickstartEntry[] entries)
    {
        _manifestClient.GetManifestAsync(Arg.Any<CancellationToken>())
            .Returns(new QuickstartManifest(entries));
    }

    private Task<int> InvokeAsync(params string[] args)
    {
        var cmd = new QuickstartInfoCommand(_interaction, _manifestClient);
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        ParseResult result = root.Parse(new[] { cmd.Name }.Concat(args).ToArray());
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return result.InvokeAsync(config);
    }
}
