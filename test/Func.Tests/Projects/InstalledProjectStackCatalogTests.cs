// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Tests.Projects;

public class InstalledProjectStackCatalogTests
{
    private static readonly IProjectStack _go = new TestProjectStack("go", "Go", "native", new LanguageDescriptor("go", "Go", "golang"));

    [Fact]
    public void EmptyInventory_ReturnsNoStack()
    {
        InstalledProjectStackCatalog catalog = new([]);

        catalog.FindStack("go").Should().BeNull();
        catalog.ResolveProjectStack(new()).Should().BeNull();
    }

    [Fact]
    public void Constructor_NullInventory_Throws()
        => FluentActions.Invoking(() => new InstalledProjectStackCatalog(null!)).Should().ThrowExactly<ArgumentNullException>().WithParameterName("stacks");

    [Fact]
    public void Constructor_SnapshotsRegistrations()
    {
        List<IProjectStack> registered = [_go];
        InstalledProjectStackCatalog catalog = new(registered);
        registered.Clear();

        catalog.Stacks.Should().Equal(_go);
    }

    [Fact]
    public void Lookup_CompetingProvidersWithEqualDescriptors_RejectsBoth()
    {
        InstalledProjectStackCatalog catalog = new([_go, new TestProjectStack("GO", "Renamed", "comet", new LanguageDescriptor("go", "Go"))]);

        FluentActions.Invoking(() => catalog.FindStack("go")).Should().Throw<ProjectStackResolutionException>().WithMessage("*Multiple*'GO'*");
    }

    [Fact]
    public void Lookup_CompetingPackageManifests_ReportsBothOwnersEvenWhenOnlyOneRegisteredCapabilities()
    {
        var first = TestWorkloads.CreateInfo("Contoso.DevKit") with { Stack = _go.Descriptor };
        var other = TestWorkloads.CreateInfo("Other.DevKit") with { Stack = new StackDescriptor("GO", "other-host", "Other Label") };
        InstalledProjectStackCatalog catalog = new([_go], [first, other]);

        FluentActions.Invoking(() => catalog.FindStack("go")).Should().Throw<ProjectStackResolutionException>()
            .WithMessage("*'go'*Contoso.DevKit*Other.DevKit*");
    }

    [Theory]
    [InlineData("GO", true)]
    [InlineData("native", false)]
    [InlineData("Something Else", false)]
    public void FindStack_UsesOnlyCliId(string token, bool matches)
    {
        InstalledProjectStackCatalog catalog = new([_go]);

        (catalog.FindStack(token) == _go).Should().Be(matches);
    }

    [Fact]
    public void ResolveProjectStack_HostRuntimeMayBeAnotherStacksCliId()
    {
        IProjectStack other = new TestProjectStack("native", "Other", "other-host", new LanguageDescriptor("go", "Go"));
        InstalledProjectStackCatalog catalog = new([other, _go]);

        catalog.ResolveProjectStack(new(WorkerRuntime: " NATIVE ")).Should().BeSameAs(_go);
        catalog.ResolveProjectStack(new(HasProjectConfig: true, StackIdentifier: "NATIVE")).Should().BeSameAs(other);
    }

    [Fact]
    public void ResolveProjectStack_UnknownHostRuntimeCannotFallbackToExplicitChoice()
    {
        InstalledProjectStackCatalog catalog = new([_go]);

        FluentActions.Invoking(() => catalog.ResolveProjectStack(new(WorkerRuntime: "go"), _go))
            .Should().Throw<ProjectStackResolutionException>().WithMessage("*--stack 'go'*FUNCTIONS_WORKER_RUNTIME in local.settings.json 'go'*");
    }

    [Fact]
    public void ResolveProjectStack_SharedHostRuntime_RequiresMatchingSelection()
    {
        IProjectStack other = new TestProjectStack("comet", "Comet", "native", new LanguageDescriptor("comet", "Comet"));
        InstalledProjectStackCatalog catalog = new([other, _go]);
        ProjectStackSettings settings = new(WorkerRuntime: "native");

        FluentActions.Invoking(() => catalog.ResolveProjectStack(settings)).Should().Throw<ProjectStackResolutionException>().WithMessage("*FUNCTIONS_WORKER_RUNTIME*multiple stacks*");
        catalog.ResolveProjectStack(settings, _go).Should().BeSameAs(_go);
    }

    [Fact]
    public void ResolveLanguage_OnlyLooksAtSelectedStack_ReportsAmbiguousInputThere()
    {
        LanguageDescriptor comet = new("comet-script", "Comet Only", "go", "script");
        LanguageDescriptor other = new("other-script", "Other", "script");
        IProjectStack stack = new TestProjectStack("comet", "Comet", null, comet, other);

        InstalledProjectStackCatalog.ResolveLanguage(stack, " GO ", "--language").Should().BeSameAs(comet);
        InstalledProjectStackCatalog.ResolveLanguage(_go, " GO ", "--language").LanguageIdentifier.Should().Be("go");
        FluentActions.Invoking(() => InstalledProjectStackCatalog.ResolveLanguage(stack, "script", "--language"))
            .Should().Throw<ProjectStackResolutionException>().WithMessage("*ambiguous*comet-script*other-script*");
        FluentActions.Invoking(() => InstalledProjectStackCatalog.ResolveLanguage(stack, "Comet Only", "--language"))
            .Should().Throw<ProjectStackResolutionException>().WithMessage("*not supported*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Lookup_MissingOrDuplicateCanonicalLanguages_RejectsInvalidProvider(bool duplicate)
    {
        IProjectStack stack = new TestProjectStack("comet", "Comet", languages: duplicate ? [new("js", "left"), new("JS", "right")] : []);
        InstalledProjectStackCatalog catalog = new([stack]);

        FluentActions.Invoking(() => catalog.FindStack("comet")).Should().Throw<ProjectStackResolutionException>().WithMessage("*language*");
    }
}
