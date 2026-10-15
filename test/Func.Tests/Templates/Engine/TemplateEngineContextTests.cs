// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public class TemplateEngineContextTests
{
    private static readonly WorkingDirectory _directory = WorkingDirectory.FromExplicit(Path.Combine(Path.GetTempPath(), "func-context"));

    [Fact]
    public void Constructor_NullCommandDirectory_Throws()
    {
        Action act = () => _ = new TemplateEngineContext(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("commandDirectory");
    }

    [Fact]
    public void ProjectContext_WithoutLanguage_LeavesLanguageUnresolved()
    {
        TemplateEngineProjectContext project = new(_directory, "node");

        project.Language.Should().BeNull();
    }

    [Fact]
    public void ProjectContext_WithoutStack_LeavesStackUnresolved()
    {
        TemplateEngineProjectContext project = new(_directory, stack: null);

        project.Stack.Should().BeNull();
    }

    [Fact]
    public void ProjectContext_NullRootDirectory_Throws()
    {
        Action act = () => _ = new TemplateEngineProjectContext(null!, "node");

        act.Should().Throw<ArgumentNullException>().WithParameterName("rootDirectory");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ProjectContext_BlankStack_Throws(string stack)
    {
        Action act = () => _ = new TemplateEngineProjectContext(_directory, stack);

        act.Should().Throw<ArgumentException>().WithParameterName("stack");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ProjectContext_BlankLanguage_Throws(string language)
    {
        Action act = () => _ = new TemplateEngineProjectContext(_directory, "node", language);

        act.Should().Throw<ArgumentException>().WithParameterName("language");
    }

    [Theory]
    [InlineData("", "4.35.0", "id")]
    [InlineData("  ", "4.35.0", "id")]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle", "", "version")]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle", "  ", "version")]
    public void BundleContext_BlankValue_Throws(string id, string version, string parameterName)
    {
        Action act = () => _ = new TemplateEngineBundleContext(id, version);

        act.Should().Throw<ArgumentException>().WithParameterName(parameterName);
    }

    [Theory]
    [InlineData("[4.*, 5.0.0)")]
    [InlineData("[4.0,5.0)")]
    [InlineData("not-a-version")]
    public void BundleContext_InvalidResolvedVersion_ThrowsAtContextBoundary(string version)
    {
        Action act = () => _ = new TemplateEngineBundleContext("bundle", version);

        act.Should().Throw<ArgumentException>().WithParameterName("version").WithMessage("*resolved bundle version*");
    }

    [Theory]
    [InlineData("4.20")]
    [InlineData("4.20.0")]
    [InlineData("4.20.0-preview.1")]
    public void BundleContext_ResolvedVersion_PreservesHostValue(string version)
    {
        TemplateEngineBundleContext context = new("bundle", version);

        context.Version.Should().Be(version);
        context.ResolvedVersion.ToString().Should().StartWith("4.20");
    }
}
