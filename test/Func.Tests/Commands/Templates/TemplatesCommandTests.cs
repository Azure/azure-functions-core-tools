// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Templates;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Packages;
using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Templates;

public sealed class TemplatesCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly TemplateCatalog _catalog = Substitute.For<TemplateCatalog>(
        Options.Create(new TemplateCatalogOptions()), (Func<NuGet.Configuration.PackageSource, NuGetProtocolSourceClient>)(_ => null!));

    [Fact]
    public void Registration_ExposesOnlyTemplatesParentAsTopLevelCommand()
    {
        var services = new ServiceCollection().AddBuiltInCommands();

        services.Where(d => d.ServiceType == typeof(FuncCliCommand)).Should()
            .Contain(d => d.ImplementationType == typeof(TemplatesCommand))
            .And.NotContain(d => d.ImplementationType == typeof(TemplatesSearchCommand)
                || d.ImplementationType == typeof(TemplatesInstallCommand) || d.ImplementationType == typeof(TemplatesUninstallCommand));
    }

    [Fact]
    public async Task Search_EmitsOnlyApprovedJsonFieldsWithoutChannelExpansion()
    {
        _catalog.SearchAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns([new CatalogSearchResult("test.templates", NuGetVersion.Parse("1.0.0"), "Test", "Description",
                ["not-exposed"], new NuGet.Configuration.PackageSource("https://feed.test/index.json"))]);
        var command = new TemplatesSearchCommand(_catalog, _interaction);

        int exitCode = await InvokeAsync(command, "hello", "--source", "https://feed.test/index.json", "--format", "json");

        exitCode.Should().Be(0);
        using var json = JsonDocument.Parse(_interaction.Lines.Single()["JSON: ".Length..]);
        json.RootElement[0].EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("packageId", "latestVersion", "title", "description");
        await _catalog.Received(1).SearchAsync("hello", "https://feed.test/index.json", null, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("plain")]
    public async Task Search_PlainFormatRendersCards(string? format)
    {
        _catalog.SearchAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns([new CatalogSearchResult("test.templates", NuGetVersion.Parse("1.0.0"), "Test", "Description",
                [], new NuGet.Configuration.PackageSource("https://feed.test/index.json"))]);
        var command = new TemplatesSearchCommand(_catalog, _interaction);

        int exitCode = await InvokeAsync(command, format is null ? [] : ["--format", format]);

        exitCode.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.Contains("Package ID: ") && l.Contains("test.templates"))
            .And.NotContain(l => l.StartsWith("JSON: "));
    }

    [Fact]
    public async Task Search_InvalidFormatFailsBeforeQueryingCatalog()
    {
        var command = new TemplatesSearchCommand(_catalog, _interaction);

        Func<Task> act = () => InvokeAsync(command, "--format", "xml");

        (await act.Should().ThrowAsync<GracefulException>()).Which.Message.Should().Contain("plain, json");
        await _catalog.DidNotReceive().SearchAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_InvalidSourcePreservesInnerException()
    {
        var failure = new ArgumentException("Use an HTTP(S) feed.");
        _catalog.SearchAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        var command = new TemplatesSearchCommand(_catalog, _interaction);

        Func<Task> act = () => InvokeAsync(command);

        (await act.Should().ThrowAsync<GracefulException>()).Which.InnerException.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task Install_ForwardsExactOptionsAndReportsNoOp()
    {
        var packages = CreatePackages();
        packages.InstallAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var command = new TemplatesInstallCommand(packages, _interaction);

        int exitCode = await InvokeAsync(command, "test.templates", "--version", "1.0.0-preview.1", "--source", "https://feed.test/index.json");

        exitCode.Should().Be(0);
        await packages.Received(1).InstallAsync("test.templates", "1.0.0-preview.1", "https://feed.test/index.json", false, Arg.Any<CancellationToken>());
        _interaction.Lines.Should().Contain(l => l.Contains("already installed"));
    }

    [Fact]
    public async Task Uninstall_ManagedIdentityFailurePreservesInnerException()
    {
        var packages = CreatePackages();
        var failure = new TemplatePackageException("Not installed.");
        packages.UninstallAsync("test.templates", Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        var command = new TemplatesUninstallCommand(packages, _interaction);

        Func<Task> act = () => InvokeAsync(command, "test.templates");

        (await act.Should().ThrowAsync<GracefulException>()).Which.InnerException.Should().BeSameAs(failure);
        await packages.Received(1).UninstallAsync("test.templates", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Parent_ContainsOnlyApprovedSubcommandsWithoutProjectPath()
    {
        var packages = CreatePackages();
        var parent = new TemplatesCommand(new TemplatesSearchCommand(_catalog, _interaction),
            new TemplatesInstallCommand(packages, _interaction), new TemplatesUninstallCommand(packages, _interaction));

        parent.Subcommands.Select(c => c.Name).Should().Equal("search", "install", "uninstall");
        parent.Arguments.Should().BeEmpty();
        parent.Subcommands.SelectMany(c => c.Options).Should().NotContain(o => o.Name == "--stack" || o.Name == "--skip");
        parent.Subcommands.Single(c => c.Name == "search").Options.Should().Contain(o => o.Name == "--format")
            .And.NotContain(o => o.Name == "--json");
    }

    private TemplatePackageService CreatePackages()
        => Substitute.For<TemplatePackageService>(_catalog, new TemplatePackageValidator(), Substitute.For<ITemplaterFactory>(),
            Substitute.For<IFileSystem>(), Substitute.For<IWorkloadTemplateSource>(), new TemplatePackageVerifier());

    private static Task<int> InvokeAsync(FuncCliCommand command, params string[] arguments)
    {
        var root = new RootCommand();
        root.Subcommands.Add(command);
        return root.Parse([command.Name, .. arguments]).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
    }
}
