// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeTemplateWorkflowRegistryTests
{
    [Fact]
    public void FindItem_UsesCanonicalInstalledStackAndRefusesMissingInstallation()
    {
        var provider = Substitute.For<INativeItemWorkflowProvider>();
        provider.Stack.Returns("node");
        NativeTemplateWorkflowRegistry registry = new(Catalog(), [provider], []);

        registry.FindItem("node-runtime").Should().BeSameAs(provider);
        Action missing = () => registry.FindItem("uninstalled");
        missing.Should().Throw<InvalidOperationException>().WithMessage("*not installed*");
    }

    [Fact]
    public void FindItem_DuplicateProviders_RefusesInsteadOfPickingRegistrationOrder()
    {
        var first = Substitute.For<INativeItemWorkflowProvider>();
        first.Stack.Returns("node");
        var second = Substitute.For<INativeItemWorkflowProvider>();
        second.Stack.Returns("NODE");
        NativeTemplateWorkflowRegistry registry = new(Catalog(), [first, second], []);

        Action act = () => registry.FindItem("node");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Multiple*");
    }

    [Fact]
    public void FindProject_MissingOwnedProvider_RefusesRatherThanSkippingPreparation()
    {
        NativeTemplateWorkflowRegistry registry = new(Catalog(), [], []);

        Action act = () => registry.FindProject(Project());

        act.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*No CLI-owned project workflow*");
    }

    [Fact]
    public void FindProject_WrongDeclaredStack_RefusesMismatchedHandler()
    {
        var provider = Substitute.For<INativeProjectWorkflowProvider>();
        provider.Stack.Returns("node");
        provider.Handles(Arg.Any<TemplateCatalogEntry>()).Returns(true);
        NativeTemplateWorkflowRegistry registry = new(Catalog(), [], [provider]);
        var entry = Project() with { ProjectConfigurations = [new(0, "host.json", "python", "Python")] };

        Action act = () => registry.FindProject(entry);

        act.Should().Throw<InvalidOperationException>().WithMessage("*canonical stack*");
    }

    [Fact]
    public void FindProject_UninstalledOtherStackProvider_DoesNotInterceptCandidateQualification()
    {
        var provider = Substitute.For<INativeProjectWorkflowProvider>();
        provider.Stack.Returns("uninstalled");
        provider.Handles(Arg.Any<TemplateCatalogEntry>()).Returns(true);
        NativeTemplateWorkflowRegistry registry = new(Catalog(), [], [provider]);

        registry.FindProject(Project() with { ProjectConfigurations = null }).Should().BeNull();
        provider.DidNotReceive().Handles(Arg.Any<TemplateCatalogEntry>());
    }

    [Theory]
    [InlineData("name")]
    [InlineData("target")]
    [InlineData("preview")]
    [InlineData("force")]
    public void InvocationDiagnostic_ProviderCannotRedirectUserHostInputs(string changed)
    {
        var directory = WorkingDirectory.FromExplicit(Path.Combine(Path.GetTempPath(), "workflow-original"));
        TemplateInvocationRequest original = new(directory, new Dictionary<string, string?>(), "Original", DryRun: true);
        var prepared = original with
        {
            Name = changed == "name" ? "Other" : original.Name,
            OutputDirectory = changed == "target" ? WorkingDirectory.FromExplicit(Path.Combine(Path.GetTempPath(), "other")) : directory,
            DryRun = changed == "preview" ? false : original.DryRun,
            Force = changed == "force",
        };

        NativeTemplateWorkflowRegistry.InvocationDiagnostic(original, prepared).Should().NotBeNull();
    }

    [Fact]
    public void InvocationDiagnostic_DistinguishesExplicitUserValueFromAuthoredDefault()
    {
        var directory = WorkingDirectory.FromExplicit(Path.Combine(Path.GetTempPath(), "workflow-original"));
        TemplateParameterDefinition framework = new("Framework", "target-framework", null, null, "choice",
            [new("net8.0", null, null), new("net10.0", null, null)], "net8.0", false, false, false);
        var parser = new TemplateArgumentParser();
        var explicitValue = parser.Parse(new("Source", [framework], new HashSet<string>(), ["--target-framework", "net8.0"]));
        var defaultValue = parser.Parse(new("Source", [framework], new HashSet<string>(), []));
        TemplateInvocationRequest original = new(directory, explicitValue.ValuesByCanonicalName, "Original", DryRun: true);
        var prepared = original with { Parameters = new Dictionary<string, string?> { ["Framework"] = "net10.0" } };

        NativeTemplateWorkflowRegistry.InvocationDiagnostic(original, prepared, explicitValue).Should().Contain("explicit canonical");
        NativeTemplateWorkflowRegistry.InvocationDiagnostic(original, prepared, defaultValue).Should().BeNull();
    }

    private static TemplateCatalogEntry Project() => new("Owned.Project", "Owned project", null, TemplateType.Project, [], null,
        "JavaScript", 0, new("Owned.Package", "1.0.0"), false, [], null)
    {
        ProjectConfigurations = [new(0, "host.json", "node", "JavaScript")],
    };

    private static InstalledProjectStackCatalog Catalog()
    {
        var stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns("node");
        stack.DisplayName.Returns("Node");
        stack.SupportedLanguages.Returns(["JavaScript"]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>());
        stack.WorkerRuntimeAliases.Returns(["node-runtime"]);
        return new([stack]);
    }
}
