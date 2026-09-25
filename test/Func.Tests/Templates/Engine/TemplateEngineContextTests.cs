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
}
