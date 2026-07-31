// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Search;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Search;

public class FuncTemplateSearchServiceTests
{
    private readonly IFuncSearchIndexProvider _indexProvider = Substitute.For<IFuncSearchIndexProvider>();
    private readonly IFuncTemplateFeedSearch _feedSearch = Substitute.For<IFuncTemplateFeedSearch>();
    private readonly IFuncTemplatePackageService _packageService = Substitute.For<IFuncTemplatePackageService>();

    [Fact]
    public async Task SearchAsync_TermMatchesTemplate_ReturnsPackage()
    {
        GivenIndex(Package("Contoso.Templates", "1.0.0", Template("Queue trigger", ["queue"], stack: "node")));
        GivenInstalled();

        FuncSearchResults results = await Search("queue");

        results.Packages.Should().ContainSingle().Which.PackageId.Should().Be("Contoso.Templates");
    }

    [Fact]
    public async Task SearchAsync_TermDoesNotMatch_ReturnsNoPackages()
    {
        GivenIndex(Package("Contoso.Templates", "1.0.0", Template("Queue trigger", ["queue"], stack: "node")));
        GivenInstalled();

        FuncSearchResults results = await Search("cosmos");

        results.Packages.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_EmptyTerm_ListsAllPackages()
    {
        GivenIndex(
            Package("Contoso.Templates", "1.0.0", Template("Queue trigger", ["queue"], stack: "node")),
            Package("Fabrikam.Templates", "1.0.0", Template("Timer trigger", ["timer"], stack: "python")));
        GivenInstalled();

        FuncSearchResults results = await Search(term: null);

        results.Packages.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchAsync_PackageNotInstalled_AnnotatesNotInstalled()
    {
        GivenIndex(Package("Contoso.Templates", "1.0.0", Template("Queue trigger", ["queue"], stack: "node")));
        GivenInstalled();

        FuncSearchResults results = await Search("queue");

        results.Packages[0].Installed.Should().BeOfType<FuncTemplateInstalledState.NotInstalled>();
    }

    [Fact]
    public async Task SearchAsync_PackageInstalledSameVersion_AnnotatesInstalled()
    {
        GivenIndex(Package("Contoso.Templates", "1.0.0", Template("Queue trigger", ["queue"], stack: "node")));
        GivenInstalled(new InstalledTemplatePackage("Contoso.Templates", "1.0.0", Source: null, LastChanged: null));

        FuncSearchResults results = await Search("queue");

        FuncTemplateInstalledState state = results.Packages[0].Installed;
        state.Should().BeOfType<FuncTemplateInstalledState.Installed>()
            .Which.Version.Should().Be("1.0.0");
    }

    [Fact]
    public async Task SearchAsync_PackageInstalledOlderVersion_AnnotatesUpdateAvailable()
    {
        GivenIndex(Package("Contoso.Templates", "2.0.0", Template("Queue trigger", ["queue"], stack: "node")));
        GivenInstalled(new InstalledTemplatePackage("Contoso.Templates", "1.0.0", Source: null, LastChanged: null));

        FuncSearchResults results = await Search("queue");

        FuncTemplateInstalledState.UpdateAvailable update = results.Packages[0].Installed
            .Should().BeOfType<FuncTemplateInstalledState.UpdateAvailable>().Subject;
        update.InstalledVersion.Should().Be("1.0.0");
        update.AvailableVersion.Should().Be("2.0.0");
    }

    private FuncTemplateSearchService CreateService()
        => new(_indexProvider, _feedSearch, _packageService, Substitute.For<ILogger<FuncTemplateSearchService>>());

    private Task<FuncSearchResults> Search(string? term)
        => CreateService().SearchAsync(new FuncSearchRequest(term, Source: null), CancellationToken.None);

    private void GivenIndex(params FuncSearchPackage[] packages)
        => _indexProvider.GetIndexAsync(Arg.Any<CancellationToken>()).Returns(new FuncSearchIndex("2.0", packages));

    private void GivenInstalled(params InstalledTemplatePackage[] installed)
        => _packageService.ListInstalledAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<InstalledTemplatePackage>>(installed);

    private static FuncSearchPackage Package(string name, string version, params FuncSearchTemplate[] templates)
        => new(name, version, Owners: ["Microsoft"], Description: null, templates);

    private static FuncSearchTemplate Template(string name, IReadOnlyList<string> shortNames, string stack)
        => new(
            Identity: name.Replace(" ", ".", StringComparison.Ordinal),
            Name: name,
            ShortNameList: shortNames,
            Author: "Microsoft",
            Description: null,
            Classifications: [],
            Tags: new Dictionary<string, string> { ["azfunc-stack"] = stack, ["language"] = "javascript" });
}
